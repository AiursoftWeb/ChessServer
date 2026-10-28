using System.Text.Json;
using Aiursoft.AiurObserver;
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

    public async Task ClaimGuestAsync(Guid guestId, Player registered)
    {
        foreach (var pair in database.GetRooms())
        {
            await pair.Value.MutationLock.WaitAsync();
            try
            {
                // Accept can replace the waiting room while retaining this same mutation lock.
                var room = database.GetChallenge(pair.Key);
                if (room == null) continue;
                var creator = room.Creator;
                var accepted = room as AcceptedChallenge;
                var accepter = accepted?.Accepter;
                var claimCreator = creator.IsGuest && creator.Id == guestId;
                var claimAccepter = accepter?.IsGuest == true && accepter.Id == guestId;
                if (!claimCreator && !claimAccepter) continue;
                // One signed-in account may not acquire both seats in the same game.
                if (claimCreator && accepter?.Id == registered.Id || claimAccepter && creator.Id == registered.Id)
                    throw new InvalidOperationException("This account already holds the other seat. Use a different account to continue this game.");
                // Finishing as a guest is final: signing in later must not retroactively create personal history.
                if (accepted is { Game.Board.IsEndGame: true, Archived: false }) await history.SaveAsync(accepted);
                var before = RoomSnapshot.Capture(room);
                try
                {
                    if (accepted != null)
                    {
                        lock (accepted.Game.MovePieceLock)
                        {
                            if (claimCreator) room.Creator = registered;
                            if (claimAccepter) accepted.Accepter = registered;
                            if (accepted.DrawOfferedBy == guestId) accepted.DrawOfferedBy = registered.Id;
                            if (accepted.RematchOfferedBy == guestId) accepted.RematchOfferedBy = registered.Id;
                            accepted.Revision++;
                            accepted.Game.SearchCancellation?.Cancel();
                        }
                    }
                    else room.Creator = registered;
                    await StoreAsync(pair.Key, room);
                }
                catch
                {
                    if (accepted != null) before.Apply(accepted);
                    else room.Creator = creator;
                    context.ChangeTracker.Clear();
                    throw;
                }
                if (accepted != null) await NotifyAsync(accepted, new ChallengeContext(accepted, pair.Key));
            }
            finally { pair.Value.MutationLock.Release(); }
        }
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

    private async Task StageAsync(int id, Challenge room)
    {
        var row = await context.GameSessions.FindAsync(id);
        if (row == null) { row = new GameSession { Id = id }; context.GameSessions.Add(row); }
        // The shared scoped context commits the session and any new completed record together.
        if (room is AcceptedChallenge accepted) await history.SaveAsync(accepted, deferCommit: true);
        var snapshot = RoomSnapshot.Capture(room);
        if (room is AcceptedChallenge { Game.Board.IsEndGame: true }) snapshot.HistoryFinalized = true;
        row.State = JsonSerializer.Serialize(snapshot);
    }

    private async Task StoreAsync(int id, Challenge room)
    {
        await StageAsync(id, room);
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
        if (command.Kind is "offer-rematch" or "accept-rematch" or "decline-rematch")
            return await RematchAsync(id, player, command);
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
                var fingerprint = Fingerprint(command);
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
                    if (command.ExpectedRevision != null && command.ExpectedRevision != room.Revision) error = "stale";
                    else switch (command.Kind)
                    {
                        case "move":
                            if (room.Game.Board.MovesToSan.Count != command.ExpectedPly) { error = "stale"; break; }
                            var move = command.Move?.Replace("0", "O");
                            if (room.GetTurnPlayer().Id != player || string.IsNullOrWhiteSpace(move) || move.Length > 20 || !room.Game.Board.IsValidMove(move)) { error = "illegal"; break; }
                            while (room.MoveClocks.Count < room.Game.Board.MovesToSan.Count) room.MoveClocks.Add(null);
                            room.MoveClocks.Add(new MoveClock(room.WhiteRemaining.Ticks, room.BlackRemaining.Ticks));
                            room.Game.Board.Move(move);
                            if (room.IsTimed)
                            {
                                if (side == PieceColor.White) room.WhiteRemaining += TimeSpan.FromSeconds(room.IncrementSeconds);
                                else room.BlackRemaining += TimeSpan.FromSeconds(room.IncrementSeconds);
                            }
                            if (room.DrawOfferedBy != player) room.DrawOfferedBy = null;
                            room.UpdateClock(database.ClockNow);
                            break;
                        case "undo":
                            var undoPly = room.UndoPly;
                            if (computer || room.Difficulty == null || room.Creator.Id != player || undoPly < 0) { error = "cannot-undo"; break; }
                            var clocks = room.MoveClocks[undoPly]!;
                            var board = Game.NewBoard();
                            foreach (var san in room.Game.Board.MovesToSan.Take(undoPly)) board.Move(san);
                            room.Game.SearchCancellation?.Cancel();
                            room.Game.Board = board; room.ClearPgnCache();
                            room.WhiteRemaining = TimeSpan.FromTicks(clocks.WhiteTicks); room.BlackRemaining = TimeSpan.FromTicks(clocks.BlackTicks);
                            room.LastClockUpdate = database.ClockNow;
                            if (undoPly == 0) room.AbortAt = database.ClockNow.AddMinutes(1);
                            room.MoveClocks.RemoveRange(undoPly, room.MoveClocks.Count - undoPly);
                            room.DrawOfferedBy = null; room.OutcomeReason = null;
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

    private static string Fingerprint(GameCommand command) => $"{command.Kind}:{command.ExpectedPly}:{command.Move}"
        + (command.ExpectedRevision is { } revision ? $":{revision}" : string.Empty);

    private async Task<(ChallengeContext State, string? Error)> RematchAsync(int id, Guid player, GameCommand command)
    {
        var room = database.GetAcceptedChallenge(id) ?? throw new InvalidOperationException("Game not found.");
        await room.MutationLock.WaitAsync();
        var nextId = (int?)null;
        var before = RoomSnapshot.Capture(room);
        try
        {
            string? error = null;
            var create = false;
            lock (room.Game.MovePieceLock)
            {
                var previous = room.Receipts.FirstOrDefault(r => r.Id == command.Id);
                var fingerprint = Fingerprint(command);
                if (room.GetPlayerColor(player) == "m" || room.Difficulty != null && room.Creator.Id != player) error = "spectator";
                else if (command.Id == Guid.Empty || previous != null && previous.Fingerprint != fingerprint) error = "invalid-command";
                else if (!room.Game.Board.IsEndGame) error = "not-ended";
                else if (previous == null && room.NextGameId == null && command.ExpectedRevision != null && command.ExpectedRevision != room.Revision) error = "stale";
                else if (previous == null && room.NextGameId == null)
                {
                    switch (command.Kind)
                    {
                        case "offer-rematch":
                            if (room.Difficulty != null || room.RematchOfferedBy != null && room.RematchOfferedBy != player) create = true;
                            else room.RematchOfferedBy = player;
                            break;
                        case "accept-rematch":
                            if (room.RematchOfferedBy == null || room.RematchOfferedBy == player) error = "no-rematch";
                            else create = true;
                            break;
                        case "decline-rematch": room.RematchOfferedBy = null; break;
                    }
                    if (error == null)
                    {
                        room.Revision++; room.LastActivity = DateTimeOffset.UtcNow;
                        room.Receipts.Add(new CommandReceipt(command.Id, fingerprint));
                        if (room.Receipts.Count > 128) room.Receipts.RemoveAt(0);
                    }
                }
            }
            if (error == null)
            {
                if (create)
                {
                    var next = new AcceptedChallenge(room.Creator, room.Accepter, room.Message,
                        room.CreatorIsWhite ? RoleRule.AccepterWhite : RoleRule.CreatorWhite, room.TimeLimit,
                        room.Permission, new AsyncObservable<string>())
                    { Difficulty = room.Difficulty, IncrementSeconds = room.IncrementSeconds, IsPersisted = false };
                    try { nextId = database.CreateChallenge(next); }
                    catch (InvalidOperationException)
                    {
                        before.Apply(room);
                        return (new ChallengeContext(room, id), "rooms-full");
                    }
                    room.NextGameId = nextId; room.RematchOfferedBy = null;
                    // Commit both the old room's link and the new room in one transaction before exposing either.
                    await StageAsync(id, room);
                    await StoreAsync(nextId.Value, next);
                }
                else await StoreAsync(id, room);
            }
            var result = new ChallengeContext(room, id);
            if (error == null) await NotifyAsync(room, result);
            return (result, error);
        }
        catch
        {
            before.Apply(room); context.ChangeTracker.Clear();
            if (nextId != null) database.DeleteChallenge(nextId.Value);
            throw;
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
