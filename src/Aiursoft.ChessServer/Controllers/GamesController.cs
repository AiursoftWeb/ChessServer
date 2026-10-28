using System.Text.Json;
using Aiursoft.AiurObserver;
using Aiursoft.AiurObserver.WebSocket.Server;
using Aiursoft.ChessServer.Data;
using Aiursoft.ChessServer.Models;
using Aiursoft.ChessServer.Models.ViewModels;
using Aiursoft.ChessServer.Services;
using Aiursoft.WebTools.Attributes;
using Microsoft.AspNetCore.Mvc;

namespace Aiursoft.ChessServer.Controllers;

[Route("games")]
public class GamesController(InMemoryDatabase database, ChessEngine engine, GameHistoryService history, GameSessionService sessions, ILogger<GamesController> logger) : ChessController
{
    private sealed class SocketSendQueue
    {
        public Task Tail { get; set; } = Task.CompletedTask;
    }

    [HttpGet("{id:int}.json")]
    public async Task<IActionResult> GetInfo(int id)
    {
        var state = await sessions.StateAsync(id, PlayerId);
        return state == null ? NotFound() : Ok(state);
    }

    [HttpGet("{id:int}.color")]
    public IActionResult GetColor(int id)
    {
        var room = database.GetAcceptedChallenge(id);
        return room == null ? NotFound() : Content(room.GetPlayerColor(PlayerId));
    }

    [HttpGet("{id:int}.html")]
    public IActionResult GetHtml(int id)
    {
        var room = database.GetAcceptedChallenge(id);
        return room == null ? NotFound() : this.StackView(new RoomViewModel { Id = id, Message = room.Message, IsComputer = room.Difficulty != null });
    }

    [HttpGet("{id:int}.fen")]
    public async Task<IActionResult> GetFen(int id)
    {
        var state = await sessions.StateAsync(id, PlayerId);
        return state == null ? NotFound() : Content(state.Position);
    }

    [HttpGet("{id:int}.pgn")]
    public async Task<IActionResult> GetPgn(int id)
    {
        if (await sessions.StateAsync(id, PlayerId) == null) return NotFound();
        return Content(database.GetAcceptedChallenge(id)!.Pgn());
    }

    [HttpGet("{id:int}.ascii")]
    public async Task<IActionResult> GetAscii(int id)
    {
        if (await sessions.StateAsync(id, PlayerId) == null) return NotFound();
        var room = database.GetAcceptedChallenge(id)!;
        lock (room.Game.MovePieceLock) return Content(room.Game.Board.ToAscii());
    }

    [HttpPost("{id:int}/action"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Action(int id, [FromForm] string kind, [FromForm] Guid commandId, [FromForm] long? expectedRevision = null)
    {
        var room = database.GetAcceptedChallenge(id);
        if (room == null) return NotFound();
        if (room.GetPlayerColor(PlayerId) == "m") return Forbid();
        var result = await sessions.ExecuteAsync(id, PlayerId, new GameCommand(commandId, kind, ExpectedRevision: expectedRevision));
        return result.Error == null ? Ok(result.State) : Conflict(new { result.State, result.Error });
    }

    [HttpPost("{id:int}/computer"), ValidateAntiForgeryToken]
    public async Task<IActionResult> RetryComputer(int id)
    {
        var room = database.GetAcceptedChallenge(id);
        if (room == null) return NotFound();
        if (room.Difficulty == null || room.Creator.Id != PlayerId) return Forbid();
        await RespondToMove(id, room, HttpContext.RequestAborted);
        return Ok(await sessions.StateAsync(id, PlayerId));
    }

    [HttpGet("{id:int}.ws"), EnforceWebSocket]
    public async Task GetWebSocket(int id)
    {
        var room = database.GetAcceptedChallenge(id);
        if (room == null) { Response.StatusCode = 404; return; }
        if (!IsSameOriginWebSocket()) { Response.StatusCode = 403; return; }
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(HttpContext.RequestAborted, room.Lifetime);
        var token = lifetime.Token;
        var client = await HttpContext.AcceptWebSocketClient();
        var sendQueue = new SocketSendQueue();
        async Task SendAfter(Task previous, string payload)
        {
            // Each sender observes its own failure; later sends still wait for the previous operation to finish.
            try { await previous; } catch (Exception exception) { logger.LogDebug(exception, "The previous chess socket send failed before this update."); }
            if (token.IsCancellationRequested) return;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(5));
            await client.Send(payload, deadline.Token);
        }
        Task Send(string payload)
        {
            lock (sendQueue) return sendQueue.Tail = SendAfter(sendQueue.Tail, payload);
        }
        var outgoing = room.Game.FenChangedChannel.Subscribe(Send);
        var incoming = client.Subscribe(async payload =>
        {
            if (payload.Length > 512) return;
            GameCommand? command;
            try { command = JsonSerializer.Deserialize<GameCommand>(payload); }
            catch (JsonException) { return; }
            if (command == null || command.Kind != "move") return;
            var result = await sessions.ExecuteAsync(id, PlayerId, command);
            await Send(JsonSerializer.Serialize(new { result.State, CommandId = command.Id, result.Error }));
            if (result.Error == null) await RespondToMove(id, room, token);
        });
        try
        {
            await Send(JsonSerializer.Serialize(await sessions.StateAsync(id, PlayerId)));
            if (room.Creator.Id == PlayerId) await RespondToMove(id, room, token);
            await client.Listen(token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally
        {
            outgoing.Unsubscribe(); incoming.Unsubscribe();
            await lifetime.CancelAsync();
            Task remaining;
            lock (sendQueue) remaining = sendQueue.Tail;
            try { await remaining; } catch (OperationCanceledException) { }
            using var closeDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await client.Close(closeDeadline.Token);
        }
    }

    [HttpPost("{id:int}/complete"), ValidateAntiForgeryToken, RequestSizeLimit(524288)]
    public async Task<IActionResult> Complete(int id, [FromForm] string? pgn)
    {
        var room = database.GetAcceptedChallenge(id);
        if (room == null) return NotFound();
        if (room.GetPlayerColor(PlayerId) == "m") return Forbid();
        await room.MutationLock.WaitAsync();
        try
        {
            if (!room.Game.Board.IsEndGame) return Conflict("This game is still in progress.");
            if (!string.Equals(pgn?.Replace("\r\n", "\n"), room.Pgn().Replace("\r\n", "\n"), StringComparison.Ordinal)) return BadRequest("The PGN does not match this game.");
            await history.SaveAsync(room);
            return Ok(new { room.HistoryId });
        }
        finally { room.MutationLock.Release(); }
    }

    private async Task RespondToMove(int id, AcceptedChallenge room, CancellationToken token)
    {
        string fen;
        int ply;
        long revision;
        using var search = CancellationTokenSource.CreateLinkedTokenSource(token, room.Lifetime);
        lock (room.Game.MovePieceLock)
        {
            if (room.Difficulty == null || room.Game.IsThinking || room.Game.Board.IsEndGame || room.GetTurnPlayer().Id == room.Creator.Id) return;
            room.Game.IsThinking = true; room.Game.SearchCancellation = search;
            fen = room.Game.Board.ToFen(); ply = room.Game.Board.MovesToSan.Count; revision = room.Revision;
        }
        try
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(search.Token);
            if (room.IsTimed)
            {
                var remaining = room.CreatorIsWhite ? room.BlackRemaining : room.WhiteRemaining;
                budget.CancelAfter(remaining > TimeSpan.Zero ? remaining : TimeSpan.FromMilliseconds(1));
            }
            try
            {
                var move = await engine.GetBestMoveAsync(fen, room.Difficulty.Value, budget.Token);
                await sessions.ExecuteAsync(id, room.Accepter.Id, new GameCommand(Guid.NewGuid(), "move", ply, move, revision), computer: true);
            }
            catch (OperationCanceledException) when (budget.IsCancellationRequested) { }
        }
        finally
        {
            lock (room.Game.MovePieceLock)
            {
                if (room.Game.SearchCancellation == search) { room.Game.IsThinking = false; room.Game.SearchCancellation = null; }
            }
        }
    }
}
