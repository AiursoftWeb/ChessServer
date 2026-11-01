using Aiursoft.AiurObserver;
using Aiursoft.AiurObserver.WebSocket.Server;
using Aiursoft.ChessServer.Data;
using Aiursoft.ChessServer.Models;
using Aiursoft.ChessServer.Models.ViewModels;
using Aiursoft.ChessServer.Services;
using Aiursoft.WebTools.Attributes;
using Microsoft.AspNetCore.Mvc;

namespace Aiursoft.ChessServer.Controllers;

[Route("rooms")]
public class RoomsController(ManagedRoomService rooms, InMemoryDatabase database, LlmModelCatalog catalog,
    ComputerTurnCoordinator computers) : ChessController
{
    [HttpPost("create"), ValidateAntiForgeryToken, LimitPerMin]
    public async Task<IActionResult> Create() => RedirectToAction(nameof(Room), new { id = await rooms.CreateAsync(CurrentPlayer()) });

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Room(int id)
    {
        try { await rooms.JoinAsync(id, CurrentPlayer()); }
        catch (InvalidOperationException) { return NotFound(); }
        var room = rooms.Find(id)!;
        string[] models;
        try { models = await catalog.GetModelsAsync(HttpContext.RequestAborted); }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException or InvalidOperationException or KeyNotFoundException) { models = []; }
        var game = room.CurrentGameId is { } gameId ? database.GetAcceptedChallenge(gameId) : null;
        return this.StackView(new ManagedRoomViewModel { Id = id, Message = room.Message, Models = models,
            Game = game == null ? null : new RoomViewModel { Id = room.CurrentGameId!.Value, Message = game.Message, IsComputer = game.IsComputer, Embedded = true } });
    }

    [HttpGet("{id:int}.json")]
    public async Task<IActionResult> State(int id)
    {
        try { return Ok(await rooms.StateAsync(id, PlayerId)); }
        catch (InvalidOperationException) { return NotFound(); }
    }

    [HttpPost("{id:int}/action"), ValidateAntiForgeryToken, LimitPerMin]
    public async Task<IActionResult> Action(int id, [FromForm] RoomCommand command)
    {
        if (!ModelState.IsValid) return BadRequest(new { Error = "room-settings-invalid" });
        try
        {
            await rooms.ExecuteAsync(id, CurrentPlayer(), command);
            if (rooms.Find(id) == null || command.Kind == "leave") return Ok(new { Left = true });
            if (rooms.Find(id)?.CurrentGameId is { } gameId) computers.Wake(gameId);
            return Ok(await rooms.StateAsync(id, PlayerId));
        }
        catch (InvalidOperationException exception) { return Conflict(new { Error = exception.Message }); }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException or KeyNotFoundException) { return StatusCode(503, new { Error = "room-model-unavailable" }); }
    }

    [HttpGet("{id:int}/chat.ws"), EnforceWebSocket]
    public async Task Chat(int id)
    {
        var room = rooms.Find(id);
        if (room == null || rooms.Blocked(id, PlayerId) || !room.Members.Any(m => m.Player.Id == PlayerId)) { Response.StatusCode = 404; return; }
        if (!IsSameOriginWebSocket()) { Response.StatusCode = 403; return; }
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(HttpContext.RequestAborted, room.Lifetime);
        var token = lifetime.Token;
        var client = await HttpContext.AcceptWebSocketClient();
        var player = CurrentPlayer();
        var outgoing = room.ChatChannel.Subscribe(message => rooms.Blocked(id, player.Id) ? Task.CompletedTask
            : client.Send(System.Text.Json.JsonSerializer.Serialize(new ChatMessageResponse(message, player.Id)), token));
        var incoming = client.Subscribe(async message =>
        {
            if (rooms.Blocked(id, player.Id) || !room.Members.Any(m => m.Player.Id == player.Id)
                || !(room.White is { IsAi: false } white && white.Player.Id == player.Id || room.Black is { IsAi: false } black && black.Player.Id == player.Id)
                || string.IsNullOrWhiteSpace(message) || message.Length > 2000) return;
            await room.ChatChannel.BroadcastAsync(new ChatMessage(message, player));
        });
        try { await client.Listen(token); }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        finally { incoming.Unsubscribe(); outgoing.Unsubscribe(); await client.Close(CancellationToken.None); }
    }

    [HttpGet("{id:int}.ws"), EnforceWebSocket]
    public async Task Listen(int id)
    {
        var room = rooms.Find(id);
        if (room == null || rooms.Blocked(id, PlayerId)) { Response.StatusCode = 404; return; }
        if (!IsSameOriginWebSocket()) { Response.StatusCode = 403; return; }
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(HttpContext.RequestAborted, room.Lifetime);
        var token = lifetime.Token;
        var client = await HttpContext.AcceptWebSocketClient();
        var outgoing = room.ChallengeChangedChannel.Subscribe(message => client.Send(message, token));
        try { await client.Listen(token); }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        finally { outgoing.Unsubscribe(); await client.Close(CancellationToken.None); }
    }
}
