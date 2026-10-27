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
public class GamesController(InMemoryDatabase database, ChessEngine engine, GameHistoryService history) : ChessController
{
    [HttpGet("{id:int}.json")]
    public IActionResult GetInfo(int id)
    {
        var room = database.GetAcceptedChallenge(id);
        if (room == null) return NotFound();
        lock (room.Game.MovePieceLock)
        {
            room.UpdateClock(DateTimeOffset.UtcNow);
            return Ok(new ChallengeContext(room, id));
        }
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
        return room == null ? NotFound() : this.StackView(new RoomViewModel { Id = id, Message = room.Message, PreferredMinutes = room.IsTimed ? (int)room.TimeLimit.TotalMinutes : null });
    }

    [HttpGet("{id:int}.fen")]
    public IActionResult GetFen(int id)
    {
        var room = database.GetAcceptedChallenge(id);
        if (room == null) return NotFound();
        lock (room.Game.MovePieceLock) return Content(room.Game.Board.ToFen());
    }

    [HttpGet("{id:int}.pgn")]
    public IActionResult GetPgn(int id)
    {
        var room = database.GetAcceptedChallenge(id);
        if (room == null) return NotFound();
        lock (room.Game.MovePieceLock) return Content(room.Pgn());
    }

    [HttpGet("{id:int}.ascii")]
    public IActionResult GetAscii(int id)
    {
        var room = database.GetAcceptedChallenge(id);
        if (room == null) return NotFound();
        lock (room.Game.MovePieceLock) return Content(room.Game.Board.ToAscii());
    }

    [HttpGet("{id:int}.ws"), EnforceWebSocket]
    public async Task GetWebSocket(int id)
    {
        var room = database.GetAcceptedChallenge(id);
        if (room == null) { Response.StatusCode = 404; return; }
        if (!IsSameOriginWebSocket()) { Response.StatusCode = 403; return; }
        var playerId = PlayerId;
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(HttpContext.RequestAborted, room.Lifetime);
        var token = lifetime.Token;
        var client = await HttpContext.AcceptWebSocketClient();
        var outgoing = room.Game.FenChangedChannel.Subscribe(fen => client.Send(fen, token));
        var incoming = client.Subscribe(async move =>
        {
            if (move.Length > 20 || string.IsNullOrWhiteSpace(move)) return;
            var changed = false;
            lock (room.Game.MovePieceLock)
            {
                room.UpdateClock(DateTimeOffset.UtcNow);
                move = move.Replace("0", "O");
                if (!room.Game.Board.IsEndGame && room.GetTurnPlayer().Id == playerId && room.Game.Board.IsValidMove(move))
                {
                    room.Game.Board.Move(move);
                    room.LastActivity = DateTimeOffset.UtcNow;
                    room.UpdateClock(room.LastActivity);
                    changed = true;
                }
            }
            if (changed)
            {
                await room.Game.FenChangedChannel.BroadcastAsync(Fen(room));
                await RespondToMove(room, token);
                await history.SaveAsync(room);
            }
            else
            {
                await client.Send(Fen(room), token);
            }
        });
        try
        {
            await client.Send(Fen(room), token);
            if (room.Creator.Id == playerId) await RespondToMove(room, token);
            await client.Listen(token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally
        {
            outgoing.Unsubscribe();
            incoming.Unsubscribe();
            await client.Close(CancellationToken.None);
        }
    }

    [HttpPost("{id:int}/complete"), ValidateAntiForgeryToken]
    [RequestSizeLimit(524288)]
    public async Task<IActionResult> Complete(int id, [FromForm] string pgn)
    {
        var room = database.GetAcceptedChallenge(id);
        if (room == null) return NotFound();
        if (room.GetPlayerColor(PlayerId) == "m") return Forbid();
        lock (room.Game.MovePieceLock)
        {
            room.UpdateClock(DateTimeOffset.UtcNow);
            if (!room.Game.Board.IsEndGame) return Conflict("This game is still in progress.");
            if (!string.Equals(pgn?.Replace("\r\n", "\n"), room.Pgn().Replace("\r\n", "\n"), StringComparison.Ordinal)) return BadRequest("The PGN does not match this game.");
        }
        await history.SaveAsync(room);
        return Ok(new { room.HistoryId });
    }

    private static string Fen(AcceptedChallenge room)
    {
        lock (room.Game.MovePieceLock) return room.Game.Board.ToFen();
    }

    private async Task RespondToMove(AcceptedChallenge room, CancellationToken token)
    {
        string fen;
        lock (room.Game.MovePieceLock)
        {
            room.UpdateClock(DateTimeOffset.UtcNow);
            if (room.Difficulty == null || room.Game.IsThinking || room.Game.Board.IsEndGame || room.GetTurnPlayer().Id == room.Creator.Id) return;
            room.Game.IsThinking = true;
            fen = room.Game.Board.ToFen();
        }
        try
        {
            var move = await engine.GetBestMoveAsync(fen, room.Difficulty.Value, token);
            lock (room.Game.MovePieceLock)
            {
                room.UpdateClock(DateTimeOffset.UtcNow);
                if (!token.IsCancellationRequested && !room.Game.Board.IsEndGame && room.Game.Board.ToFen() == fen && room.Game.Board.IsValidMove(move))
                {
                    room.Game.Board.Move(move);
                    room.LastActivity = DateTimeOffset.UtcNow;
                    room.UpdateClock(room.LastActivity);
                }
            }
            await history.SaveAsync(room);
            await room.Game.FenChangedChannel.BroadcastAsync(Fen(room));
        }
        finally
        {
            lock (room.Game.MovePieceLock) room.Game.IsThinking = false;
        }
    }
}
