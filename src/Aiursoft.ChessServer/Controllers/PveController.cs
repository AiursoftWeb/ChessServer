using Aiursoft.AiurObserver;
using Aiursoft.AiurObserver.WebSocket;
using Aiursoft.ChessServer.Data;
using Aiursoft.ChessServer.Models;
using Aiursoft.CSTools.Services;
using Microsoft.AspNetCore.Mvc;
using Aiursoft.AiurObserver.Extensions;
using Aiursoft.ChessServer.Services;

namespace Aiursoft.ChessServer.Controllers;

[Route("pve")]
public class PveController(
    ChessEngine engine,
    Counter counter,
    InMemoryDatabase database) : Controller
{
    [Route("new")]
    [HttpGet]
    public async Task<IActionResult> New(Guid playerId)
    {
        var computerId = Guid.NewGuid();
        var player = database.GetOrAddPlayer(playerId);
        var challenge = new Challenge(
            creator: player, 
            message: "Play against computer", 
            roleRule: RoleRule.CreatorWhite,
            timeLimit: TimeSpan.MaxValue, 
            permission: ChallengePermission.Public);
        var challengeId = counter.GetUniqueNo();
        database.CreateChallenge(challengeId, challenge);
        database.GetOrAddPlayer(computerId).NickName = "Computer";
        await database.PatchChallengeAsAcceptedAsync(challengeId, computerId);
        var acceptedChallenge = database.GetAcceptedChallenge(challengeId);
        await Task.Factory.StartNew(async () =>
        {
            ISubscription? subscription = null;
            try
            {
                var webSocketSchema = HttpContext.Request.Scheme == "https" ? "wss" : "ws";
                var webSocketEndpoint =
                    $"{webSocketSchema}://{HttpContext.Request.Host}/games/{challengeId}.ws?playerId={computerId}";
                var client = await webSocketEndpoint.ConnectAsWebSocketServer();
                subscription = client.Subscribe(async fen =>
                {
                    if (acceptedChallenge?.GetTurnPlayer().Id == computerId)
                    {
                        await Task.Delay(200);
                        var bestMove = engine.GetBestMove(fen);
                        await client.Send(bestMove);
                    }
                });
                await client.Listen();
            }
            finally
            {
                subscription?.Unsubscribe();
            }
        });
        
        return RedirectToAction(nameof(GamesController.GetHtml), "Games", new { id = challengeId });
    }
}