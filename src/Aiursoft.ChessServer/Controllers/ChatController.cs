using System.Text.Json;
using Aiursoft.AiurObserver;
using Aiursoft.AiurObserver.Extensions;
using Aiursoft.AiurObserver.WebSocket.Server;
using Aiursoft.ChessServer.Attributes;
using Aiursoft.ChessServer.Data;
using Aiursoft.ChessServer.Models;
using Aiursoft.WebTools.Attributes;
using Microsoft.AspNetCore.Mvc;

namespace Aiursoft.ChessServer.Controllers;

[Route("chats")]
public class ChatController(InMemoryDatabase database) : ControllerBase
{
    [Route("{id:int}.ws")]
    [EnforceWebSocket]
    public async Task GetWebSocket([FromRoute] int id, [FromQuery][IsGuid] string playerId)
    {
        if (!ModelState.IsValid)
        {
            return;
        }
        var playerGuid = Guid.Parse(playerId);
        var player = database.GetOrAddPlayer(playerGuid);
        var challenge = database.GetAcceptedChallenge(id);
        if (challenge == null)
        {
            return;
        }

        var pusher = await HttpContext.AcceptWebSocketClient();
        var outSub = challenge
            .ChatChannel
            .Map(t => new ChatMessageResponse(t, playerGuid))
            .Subscribe(t => pusher.Send(JsonSerializer.Serialize(t), HttpContext.RequestAborted));

        var inSub = pusher
            .Filter(t => !string.IsNullOrWhiteSpace(t))
            .Subscribe(async message =>
            {
                await challenge.ChatChannel.BroadcastAsync(new ChatMessage(message, player));
            });

        try
        {
            await pusher.Listen(HttpContext.RequestAborted);
        }
        catch (TaskCanceledException)
        {
            // Ignore. This happens when the client closes the connection.
        }
        finally
        {
            await pusher.Close(HttpContext.RequestAborted);
            outSub.Unsubscribe();
            inSub.Unsubscribe();
        }
    }
}