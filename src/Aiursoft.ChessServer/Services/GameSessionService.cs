using System.Text.Json;
using Aiursoft.ChessServer.Data;
using Aiursoft.ChessServer.Entities;
using Aiursoft.ChessServer.Models;
using Aiursoft.ChessServer.Models.ViewModels;
using Aiursoft.Scanner.Abstractions;
using Chess;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.ChessServer.Services;

public class GameSessionService(InMemoryDatabase database, TemplateDbContext context, GameHistoryService history, ILogger<GameSessionService> logger) : IScopedDependency
{
    private async Task NotifyAsync(AcceptedChallenge room, ChallengeContext state)
    {
        try { await room.Game.FenChangedChannel.BroadcastAsync(JsonSerializer.Serialize(state)); }
        catch (Exception exception) { logger.LogDebug(exception, "A disconnected chess client could not receive a committed update."); }
    }

    public async Task RestoreAsync()
    {
        database.SetNextId(await context.GameSessions.Select(g => (int?)g.Id).MaxAsync() ?? 0);
        var rows = await context.GameSessions.AsNoTracking().Where(g => g.State != null).ToListAsync();
        foreach (var row in rows)
        {
            var snapshot = JsonSerializer.Deserialize<RoomSnapshot>(row.State!) ?? throw new InvalidOperationException("Invalid saved chess session.");
            var room = snapshot.Restore();
            if (room is AcceptedChallenge accepted)
            {
                var now = DateTimeOffset.UtcNow;
                accepted.WhiteSeenAt = accepted.BlackSeenAt = now;
                if (!accepted.Game.Board.IsEndGame)
                {
                    // A host outage pauses clocks; browser disconnects do not. Crash recovery can grant at most the five-second checkpoint interval.
                    if (now > accepted.LastClockUpdate) accepted.AbortAt += now - accepted.LastClockUpdate;
                    accepted.LastClockUpdate = now;
                }
            }
            database.Restore(row.Id, room);
        }
    }

    private async Task StoreAsync(int id, Challenge room)
    {
        var row = await context.GameSessions.FindAsync(id);
        if (row == null) { row = new GameSession { Id = id }; context.GameSessions.Add(row); }
        row.State = JsonSerializer.Serialize(RoomSnapshot.Capture(room));
        // The shared scoped context commits the session and any new completed record together.
        if (room is AcceptedChallenge accepted) await history.SaveAsync(accepted, deferCommit: true);
        await context.SaveChangesAsync();
        room.IsPersisted = true;
        if (room is AcceptedChallenge checkpointed)
        {
            checkpointed.LastCheckpoint = DateTimeOffset.UtcNow;
            if (checkpointed.Game.Board.IsEndGame) checkpointed.Archived = true;
        }
    }

    public async Task<int> CreateAsync(Challenge room)
    {
        room.IsPersisted = false;
        var id = database.CreateChallenge(room);
        try { await StoreAsync(id, room); return id; }
        catch { database.DeleteChallenge(id); throw; }
    }

    public async Task AcceptAsync(int id, Player player)
    {
        var room = database.GetChallenge(id) ?? throw new InvalidOperationException("Challenge not found.");
        await room.MutationLock.WaitAsync();
        var committed = false;
        var transitioned = false;
        try
        {
            await database.AcceptAsync(id, player, async accepted => { transitioned = true; await StoreAsync(id, accepted); committed = true; });
        }
        catch (Exception exception) when (committed)
        {
            logger.LogDebug(exception, "A disconnected waiting client could not receive the accepted game notification.");
        }
        catch
        {
            context.ChangeTracker.Clear();
            if (transitioned) database.Restore(id, room);
            throw;
        }
        finally { room.MutationLock.Release(); }
    }

    public async Task DeleteAsync(int id)
    {
        var room = database.GetChallenge(id);
        if (room == null) return;
        await room.MutationLock.WaitAsync();
        try
        {
            if (room is AcceptedChallenge) throw new InvalidOperationException("Use the game controls to end this game.");
            var row = await context.GameSessions.FindAsync(id);
            if (row != null) { row.State = null; await context.SaveChangesAsync(); }
            database.DeleteChallenge(id);
        }
        finally { room.MutationLock.Release(); }
    }

    private static void Touch(AcceptedChallenge room, Guid viewer, DateTimeOffset now)
    {
        if (room.GetWhitePlayer().Id == viewer) room.WhiteSeenAt = now;
        if (room.GetBlackPlayer().Id == viewer) room.BlackSeenAt = now;
    }

    private static void Finish(AcceptedChallenge room, string reason, DateTimeOffset now, PieceColor? loser = null)
    {
        if (room.Game.Board.IsEndGame) return;
        if (loser != null && MatingMaterial.CanMate(room.Game.Board.ToFen(), loser == PieceColor.White ? 'b' : 'w')) room.Game.Board.Resign(loser);
        else room.Game.Board.Draw();
        room.OutcomeReason = reason; room.FinishedAt = now; room.LastActivity = now; room.DrawOfferedBy = null; room.Revision++;
    }

    private static void Advance(AcceptedChallenge room, DateTimeOffset now)
    {
        room.UpdateClock(now);
        if (room.Game.Board.IsEndGame) return;
        if (room.Game.Board.MovesToSan.Count == 0 && now > room.AbortAt)
            Finish(room, "Aborted", now);
        else if (room.Difficulty != null && now - (room.CreatorIsWhite ? room.WhiteSeenAt : room.BlackSeenAt) > TimeSpan.FromMinutes(2))
            Finish(room, "Abandonment", now, room.CreatorIsWhite ? PieceColor.White : PieceColor.Black);
        else if (room.Difficulty == null && now - room.WhiteSeenAt > TimeSpan.FromMinutes(10) && now - room.BlackSeenAt > TimeSpan.FromMinutes(10))
            Finish(room, "Abandoned", now);
    }

    public async Task<ChallengeContext?> StateAsync(int id, Guid viewer)
    {
        var room = database.GetAcceptedChallenge(id);
        if (room == null) return null;
        await room.MutationLock.WaitAsync();
        try
        {
            var before = RoomSnapshot.Capture(room);
            try
            {
                lock (room.Game.MovePieceLock)
                {
                    Touch(room, viewer, DateTimeOffset.UtcNow);
                    Advance(room, database.ClockNow);
                }
                if (room.Game.Board.IsEndGame && !room.Archived) await StoreAsync(id, room);
                lock (room.Game.MovePieceLock) return new ChallengeContext(room, id);
            }
            catch { before.Apply(room); context.ChangeTracker.Clear(); throw; }
        }
        finally { room.MutationLock.Release(); }
    }

    public async Task<(ChallengeContext State, string? Error)> ExecuteAsync(int id, Guid player, GameCommand command, bool computer = false)
    {
        var room = database.GetAcceptedChallenge(id) ?? throw new InvalidOperationException("Game not found.");
        await room.MutationLock.WaitAsync();
        try
        {
            var before = RoomSnapshot.Capture(room);
            try
            {
                string? error = null;
                lock (room.Game.MovePieceLock)
                {
                var fingerprint = $"{command.Kind}:{command.ExpectedPly}:{command.Move}";
                var previous = room.Receipts.FirstOrDefault(r => r.Id == command.Id);
                var color = room.GetPlayerColor(player);
                Advance(room, database.ClockNow);
                if (color == "m") error = "spectator";
                else if (command.Id == Guid.Empty) error = "invalid-command";
                else if (previous != null)
                {
                    if (previous.Fingerprint != fingerprint) error = "invalid-command";
                }
                else if (room.Game.Board.IsEndGame) error = "ended";
                else
                {
                    var side = color == "w" ? PieceColor.White : PieceColor.Black;
                    if (!computer) Touch(room, player, DateTimeOffset.UtcNow);
                    switch (command.Kind)
                    {
                        case "move":
                            if (room.Game.Board.MovesToSan.Count != command.ExpectedPly) { error = "stale"; break; }
                            var move = command.Move?.Replace("0", "O");
                            if (room.GetTurnPlayer().Id != player || string.IsNullOrWhiteSpace(move) || move.Length > 20 || !room.Game.Board.IsValidMove(move)) { error = "illegal"; break; }
                            room.Game.Board.Move(move); room.DrawOfferedBy = null;
                            room.UpdateClock(database.ClockNow);
                            break;
                        case "resign": Finish(room, "Resigned", DateTimeOffset.UtcNow, side); break;
                        case "abort":
                            if (room.Game.Board.MovesToSan.Count != 0) error = "cannot-abort";
                            else Finish(room, "Aborted", DateTimeOffset.UtcNow);
                            break;
                        case "offer-draw":
                            if (room.Difficulty != null || room.DrawOfferedBy != null) error = "cannot-offer";
                            else room.DrawOfferedBy = player;
                            break;
                        case "accept-draw":
                            if (room.DrawOfferedBy == null || room.DrawOfferedBy == player) error = "no-offer";
                            else Finish(room, "Agreement", DateTimeOffset.UtcNow);
                            break;
                        case "decline-draw":
                            if (room.DrawOfferedBy == null) error = "no-offer";
                            else room.DrawOfferedBy = null;
                            break;
                        case "claim":
                            var opponentSeen = color == "w" ? room.BlackSeenAt : room.WhiteSeenAt;
                            if (room.Difficulty != null || DateTimeOffset.UtcNow - opponentSeen < TimeSpan.FromSeconds(90)) error = "cannot-claim";
                            else Finish(room, "Abandonment", DateTimeOffset.UtcNow, side == PieceColor.White ? PieceColor.Black : PieceColor.White);
                            break;
                        default: error = "invalid-command"; break;
                    }
                    if (error == null)
                    {
                        room.Revision++; room.LastActivity = DateTimeOffset.UtcNow;
                        room.Receipts.Add(new CommandReceipt(command.Id, fingerprint));
                        if (room.Receipts.Count > 128) room.Receipts.RemoveAt(0);
                    }
                }
                }
                if (error == null || room.Game.Board.IsEndGame) await StoreAsync(id, room);
                var result = new ChallengeContext(room, id);
                if (error == null) await NotifyAsync(room, result);
                return (result, error);
            }
            catch { before.Apply(room); context.ChangeTracker.Clear(); throw; }
        }
        finally { room.MutationLock.Release(); }
    }

    public async Task CheckpointAsync()
    {
        foreach (var pair in database.GetRooms())
        {
            if (pair.Value is not AcceptedChallenge room || room.Game.Board.IsEndGame) continue;
            await room.MutationLock.WaitAsync();
            try
            {
                room.UpdateClock(database.ClockNow);
                await StoreAsync(pair.Key, room);
            }
            finally { room.MutationLock.Release(); }
        }
    }

    public async Task TickAsync()
    {
        foreach (var pair in database.GetRooms())
        {
            if (pair.Value is not AcceptedChallenge room) continue;
            await room.MutationLock.WaitAsync();
            try
            {
                var before = RoomSnapshot.Capture(room);
                try
                {
                    lock (room.Game.MovePieceLock) Advance(room, database.ClockNow);
                    if (room.Game.Board.IsEndGame && !room.Archived)
                    {
                        await StoreAsync(pair.Key, room);
                        await NotifyAsync(room, new ChallengeContext(room, pair.Key));
                    }
                    else if (!room.Game.Board.IsEndGame && DateTimeOffset.UtcNow - room.LastCheckpoint >= TimeSpan.FromSeconds(5))
                        await StoreAsync(pair.Key, room);
                }
                catch { before.Apply(room); context.ChangeTracker.Clear(); throw; }
            }
            finally { room.MutationLock.Release(); }
        }
        var previous = database.GetRooms();
        database.RemoveExpired(DateTimeOffset.UtcNow);
        foreach (var pair in previous.Where(p => database.GetChallenge(p.Key) == null))
        {
            var row = await context.GameSessions.FindAsync(pair.Key);
            if (row != null) row.State = null;
        }
        await context.SaveChangesAsync();
    }
}
