using System.Text.Json;
using Aiursoft.AiurObserver;
using Aiursoft.ChessServer.Data;
using Aiursoft.ChessServer.Entities;
using Aiursoft.ChessServer.Models;
using Aiursoft.Scanner.Abstractions;

namespace Aiursoft.ChessServer.Services;

public class ManagedRoomService(InMemoryDatabase database, GameSessionService sessions, LlmModelCatalog catalog,
    TemplateDbContext context, ILogger<ManagedRoomService> logger) : IScopedDependency
{
    public ManagedRoom? Find(int id) => database.GetChallenge(id) as ManagedRoom;
    public bool Blocked(int? id, Guid player) => id != null && Find(id.Value)?.Kicked.Contains(player) == true;
    public bool IsHost(int? id, Guid player) => id != null && Find(id.Value)?.Creator.Id == player;

    public Task<int> CreateAsync(Player host) => sessions.CreateAsync(new ManagedRoom(host));

    public async Task JoinAsync(int id, Player player)
    {
        var room = Find(id) ?? throw new InvalidOperationException("room-unavailable");
        await room.MutationLock.WaitAsync();
        try
        {
            if (!ReferenceEquals(Find(id), room)) throw new InvalidOperationException("room-unavailable");
            if (room.Kicked.Contains(player.Id)) throw new InvalidOperationException("room-kicked");
            var member = room.Members.FirstOrDefault(m => m.Player.Id == player.Id);
            if (member != null) { member.SeenAt = DateTimeOffset.UtcNow; return; }
            if (room.Members.Count >= 64) throw new InvalidOperationException("room-full");
            room.Members = [..room.Members, new RoomMember { Player = player }]; room.Version++;
            try { await sessions.StoreAsync(id, room); }
            catch { room.Members = room.Members.Where(m => m.Player.Id != player.Id).ToList(); room.Version--; throw; }
            await NotifyAsync(room);
        }
        finally { room.MutationLock.Release(); }
    }

    public async Task<object> StateAsync(int id, Guid viewer)
    {
        var room = Find(id) ?? throw new InvalidOperationException("room-unavailable");
        await room.MutationLock.WaitAsync();
        try
        {
            if (!ReferenceEquals(Find(id), room)) throw new InvalidOperationException("room-unavailable");
            if (room.Kicked.Contains(viewer)) throw new InvalidOperationException("room-kicked");
            var member = room.Members.FirstOrDefault(m => m.Player.Id == viewer);
            if (member != null) member.SeenAt = DateTimeOffset.UtcNow;
            var game = room.CurrentGameId is { } gameId ? database.GetAcceptedChallenge(gameId) : null;
            object? Seat(RoomSeat? seat) => seat == null ? null : new { seat.Player.Id, Name = seat.Description, seat.Kind, seat.Difficulty, seat.Model };
            return new { Id = id, room.Version, Name = room.Message, Host = room.Creator.Id, Viewer = viewer,
                Members = room.Members.Select(m => new { m.Player.Id, m.Player.NickName, m.Player.IsGuest }).ToArray(),
                White = Seat(room.White), Black = Seat(room.Black), room.CurrentGameId,
                Stage = game == null ? "waiting" : game.Game.Board.IsEndGame ? "finished" : "playing",
                Minutes = room.TimeLimit == TimeSpan.MaxValue ? 0 : (int)room.TimeLimit.TotalMinutes, room.IncrementSeconds,
                Public = room.Permission == ChallengePermission.Public, room.RandomColors,
                CanStart = game == null && room.White != null && room.Black != null,
                game?.Game.ComputerError, game?.HistoryId, OrganizerId = game?.Organizer?.Id };
        }
        finally { room.MutationLock.Release(); }
    }

    private Task NotifyAsync(ManagedRoom room) => NotifyCoreAsync(room);
    private async Task NotifyCoreAsync(ManagedRoom room)
    {
        try { await room.ChallengeChangedChannel.BroadcastAsync("room-changed"); }
        catch (Exception exception) { logger.LogDebug(exception, "A room viewer disconnected before receiving an update."); }
    }

    public async Task ExecuteAsync(int id, Player actor, RoomCommand command)
    {
        var room = Find(id) ?? throw new InvalidOperationException("room-unavailable");
        await room.MutationLock.WaitAsync();
        int? createdGame = null;
        var before = JsonSerializer.Deserialize<RoomSnapshot>(JsonSerializer.Serialize(RoomSnapshot.Capture(room)))!;
        try
        {
            if (!ReferenceEquals(Find(id), room)) throw new InvalidOperationException("room-unavailable");
            if (room.Kicked.Contains(actor.Id) || !room.Members.Any(m => m.Player.Id == actor.Id)) throw new InvalidOperationException("room-kicked");
            if (command.CommandId == Guid.Empty) throw new InvalidOperationException("invalid-command");
            if (room.Commands.Contains(command.CommandId)) return;
            if (command.Version != room.Version) throw new InvalidOperationException("room-stale");
            if (command.Kind == "leave") { await LeaveLockedAsync(id, room, actor.Id); return; }
            if (room.Creator.Id != actor.Id) throw new InvalidOperationException("room-host-only");
            var game = room.CurrentGameId is { } gameId ? database.GetAcceptedChallenge(gameId) : null;
            var playing = game is { Game.Board.IsEndGame: false };
            if (playing && command.Kind is "settings" or "add-ai" or "remove-ai" or "swap" or "next" or "start") throw new InvalidOperationException("room-locked");
            RoomMember Target() => room.Members.FirstOrDefault(m => m.Player.Id == command.Target) ?? throw new InvalidOperationException("room-member-missing");
            async Task InterruptAsync()
            {
                if (!playing) return;
                if (!command.Confirm) throw new InvalidOperationException("room-confirm-interruption");
                await sessions.EndManagedGameAsync(room.CurrentGameId!.Value);
                room.CurrentGameId = null;
            }
            switch (command.Kind)
            {
                case "settings":
                    if (string.IsNullOrWhiteSpace(command.Name) || command.Name.Length > 80 || !new[] { 0, 3, 5, 10, 15 }.Contains(command.Minutes)
                        || !new[] { 0, 1, 2, 3, 5, 10, 15, 30 }.Contains(command.IncrementSeconds) || command.Minutes == 0 && command.IncrementSeconds != 0)
                        throw new InvalidOperationException("room-settings-invalid");
                    room.Message = command.Name.Trim(); room.TimeLimit = command.Minutes == 0 ? TimeSpan.MaxValue : TimeSpan.FromMinutes(command.Minutes);
                    room.IncrementSeconds = command.IncrementSeconds; room.Permission = command.Public ? ChallengePermission.Public : ChallengePermission.Unlisted;
                    room.RandomColors = command.RandomColors;
                    break;
                case "transfer": room.Creator = Target().Player; break;
                case "kick":
                    var kicked = Target();
                    if (kicked.Player.Id == actor.Id) throw new InvalidOperationException("room-use-leave");
                    if (playing && game!.GetPlayerColor(kicked.Player.Id) != "m") await InterruptAsync();
                    room.Kicked = room.Kicked.Append(kicked.Player.Id).ToHashSet(); RemoveMember(room, kicked.Player.Id);
                    break;
                case "move":
                    var target = Target();
                    if (command.Seat is not ("white" or "black" or "observer")) throw new InvalidOperationException("room-settings-invalid");
                    var previous = room.White?.Player.Id == target.Player.Id ? "white" : room.Black?.Player.Id == target.Player.Id ? "black" : "observer";
                    if (previous == command.Seat) break;
                    var occupant = command.Seat == "white" ? room.White : command.Seat == "black" ? room.Black : null;
                    if (occupant?.IsAi == true && !command.Confirm) throw new InvalidOperationException("room-confirm-replace-ai");
                    await InterruptAsync();
                    var moved = new RoomSeat { Player = target.Player };
                    if (previous == "white") room.White = occupant;
                    if (previous == "black") room.Black = occupant;
                    if (command.Seat == "white") room.White = moved;
                    if (command.Seat == "black") room.Black = moved;
                    break;
                case "add-ai":
                    if (command.Seat is not ("white" or "black") || (command.Seat == "white" ? room.White : room.Black) != null) throw new InvalidOperationException("room-seat-occupied");
                    if (command.Ai is not ("llm" or "engine") || command.Ai == "engine" && command.Difficulty is < 1 or > 8) throw new InvalidOperationException("room-settings-invalid");
                    if (command.Ai == "llm" && (command.Model == null || !(await catalog.GetModelsAsync(CancellationToken.None)).Contains(command.Model, StringComparer.Ordinal)))
                        throw new InvalidOperationException("room-model-unavailable");
                    var ai = new RoomSeat { Player = new Player(Guid.NewGuid()), Kind = command.Ai,
                        Difficulty = command.Ai == "engine" ? command.Difficulty : null, Model = command.Ai == "llm" ? command.Model : null };
                    ai.Player.NickName = ai.Description;
                    if (command.Seat == "white") room.White = ai; else room.Black = ai;
                    break;
                case "remove-ai":
                    var removed = command.Seat == "white" ? room.White : command.Seat == "black" ? room.Black : null;
                    if (removed?.IsAi != true) throw new InvalidOperationException("room-settings-invalid");
                    if (command.Seat == "white") room.White = null; else room.Black = null;
                    break;
                case "swap": (room.White, room.Black) = (room.Black, room.White); break;
                case "next":
                    if (room.CurrentGameId == null || game == null || !game.Game.Board.IsEndGame) throw new InvalidOperationException("not-ended");
                    room.CurrentGameId = null; break;
                case "start":
                    if (room.CurrentGameId != null || room.White == null || room.Black == null) throw new InvalidOperationException("room-two-players");
                    if (room.RandomColors && Random.Shared.Next(2) == 0) (room.White, room.Black) = (room.Black, room.White);
                    RoomSeat Copy(RoomSeat seat) => new() { Player = seat.Player, Kind = seat.Kind, Difficulty = seat.Difficulty, Model = seat.Model };
                    var started = new AcceptedChallenge(room.White.Player, room.Black.Player, room.Message, RoleRule.CreatorWhite,
                        room.TimeLimit, room.Permission, new AsyncObservable<string>())
                    { ParentRoomId = id, Organizer = room.Creator, WhiteController = Copy(room.White), BlackController = Copy(room.Black), IncrementSeconds = room.IncrementSeconds };
                    room.CurrentGameId = createdGame = await sessions.CreateAsync(started);
                    break;
                case "retry":
                    if (game == null || !playing || game.Game.ComputerError == null) throw new InvalidOperationException("room-no-failed-turn");
                    await game.MutationLock.WaitAsync();
                    try { lock (game.Game.MovePieceLock) { game.Game.ComputerError = null; game.Interventions.Add("computer-retry"); } await sessions.StoreAsync(room.CurrentGameId!.Value, game); }
                    finally { game.MutationLock.Release(); }
                    break;
                case "interrupt": await InterruptAsync(); break;
                default: throw new InvalidOperationException("invalid-command");
            }
            room.Commands.Add(command.CommandId); if (room.Commands.Count > 128) room.Commands.RemoveAt(0);
            room.Version++; room.LastActivity = DateTimeOffset.UtcNow;
            await sessions.StoreAsync(id, room);
            await NotifyAsync(room);
        }
        catch
        {
            var restored = (ManagedRoom)before.Restore();
            room.Creator = restored.Creator; room.Message = restored.Message; room.TimeLimit = restored.TimeLimit; room.Permission = restored.Permission;
            room.Members = restored.Members; room.White = restored.White; room.Black = restored.Black; room.Kicked = restored.Kicked;
            room.CurrentGameId = restored.CurrentGameId; room.RandomColors = restored.RandomColors; room.Version = restored.Version;
            room.Commands = restored.Commands; room.IncrementSeconds = restored.IncrementSeconds;
            context.ChangeTracker.Clear();
            if (createdGame != null) await sessions.RemoveManagedSessionAsync(createdGame.Value);
            throw;
        }
        finally { room.MutationLock.Release(); }
    }

    private static void RemoveMember(ManagedRoom room, Guid player)
    {
        room.Members = room.Members.Where(m => m.Player.Id != player).ToList();
        if (room.White?.Player.Id == player) room.White = null;
        if (room.Black?.Player.Id == player) room.Black = null;
        if (room.Creator.Id == player && room.Members.Count > 0) room.Creator = room.Members.OrderByDescending(m => DateTimeOffset.UtcNow - m.SeenAt <= TimeSpan.FromSeconds(60)).ThenBy(m => m.JoinedAt).First().Player;
    }

    private async Task LeaveLockedAsync(int id, ManagedRoom room, Guid player)
    {
        if (room.Members.Count == 1) { await DestroyLockedAsync(id); return; }
        if (room.CurrentGameId is { } gameId && database.GetAcceptedChallenge(gameId) is { Game.Board.IsEndGame: false } game && game.GetPlayerColor(player) != "m")
            await sessions.EndManagedGameAsync(gameId, abandoned: true, loser: player);
        RemoveMember(room, player); room.Version++;
        await sessions.StoreAsync(id, room); await NotifyAsync(room);
    }

    private async Task DestroyLockedAsync(int id)
    {
        foreach (var pair in database.GetRooms().Where(p => p.Value is AcceptedChallenge game && game.ParentRoomId == id))
        {
            await sessions.EndManagedGameAsync(pair.Key, abandoned: true);
            await sessions.RemoveManagedSessionAsync(pair.Key);
        }
        var row = await context.GameSessions.FindAsync(id);
        if (row != null) { row.State = null; await context.SaveChangesAsync(); }
        database.DeleteChallenge(id);
    }

    public async Task TickAsync()
    {
        foreach (var pair in database.GetRooms().Where(p => p.Value is AcceptedChallenge { ParentRoomId: not null, Game.Board.IsEndGame: true }))
        {
            var game = (AcceptedChallenge)pair.Value;
            if (Find(game.ParentRoomId!.Value)?.CurrentGameId != pair.Key && game.Archived)
                await sessions.RemoveManagedSessionAsync(pair.Key);
        }
        foreach (var pair in database.GetRooms().Where(p => p.Value is ManagedRoom))
        {
            var room = (ManagedRoom)pair.Value;
            await room.MutationLock.WaitAsync();
            try
            {
                if (!ReferenceEquals(Find(pair.Key), room)) continue;
                var expired = room.Members.Where(m => DateTimeOffset.UtcNow - m.SeenAt > TimeSpan.FromSeconds(60)).Select(m => m.Player.Id).ToArray();
                if (expired.Length == room.Members.Count) await DestroyLockedAsync(pair.Key);
                else foreach (var player in expired) await LeaveLockedAsync(pair.Key, room, player);
            }
            finally { room.MutationLock.Release(); }
        }
    }
}
