using System.Text.Json;
using Aiursoft.AiurObserver;
using Aiursoft.AiurObserver.WebSocket.Server;
using Aiursoft.ChessServer.Data;
using Aiursoft.ChessServer.Models;
using Aiursoft.WebTools.Attributes;
using Microsoft.AspNetCore.Mvc;

namespace Aiursoft.ChessServer.Controllers;

[Route("chats")]
public class ChatController(InMemoryDatabase database) : ChessController
{
    [HttpGet("{id:int}.ws"), EnforceWebSocket]
    public async Task GetWebSocket(int id)
    {
        var room = database.GetAcceptedChallenge(id);
        if (room == null) { Response.StatusCode = 404; return; }
        if (!IsSameOriginWebSocket()) { Response.StatusCode = 403; return; }
        var player = CurrentPlayer();
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(HttpContext.RequestAborted, room.Lifetime);
        var token = lifetime.Token;
        var client = await HttpContext.AcceptWebSocketClient();
        var outgoing = room.ChatChannel.Subscribe(message => client.Send(
            JsonSerializer.Serialize(new ChatMessageResponse(message, player.Id)), token));
        var incoming = client.Subscribe(async message =>
        {
            if (string.IsNullOrWhiteSpace(message) || message.Length > 2000) return;
            await room.ChatChannel.BroadcastAsync(new ChatMessage(message, player));
        });
        try { await client.Listen(token); }
        finally
        {
            outgoing.Unsubscribe();
            incoming.Unsubscribe();
            await client.Close(CancellationToken.None);
        }
    }
}
