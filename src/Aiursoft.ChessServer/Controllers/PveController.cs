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
        // Add a computer player
        var computerId = Guid.NewGuid();
        database.GetOrAddPlayer(computerId).NickName = "Computer";

        // Create a challenge
        var player = database.GetOrAddPlayer(playerId);
        var challenge = new Challenge(
            creator: player, 
            message: "Play against computer", 
            roleRule: RoleRule.CreatorWhite,
            timeLimit: TimeSpan.MaxValue, 
            permission: ChallengePermission.Public);
        var challengeId = counter.GetUniqueNo();
        database.CreateChallenge(challengeId, challenge);
        
        // Let the computer accept the challenge
        await database.PatchChallengeAsAcceptedAsync(challengeId, computerId);
        
        // Get the accepted challenge
        var acceptedChallenge = database.GetAcceptedChallenge(challengeId);
        
        // Let computer respond to the player's move
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
                    // When fen changes, it means someone has made a move. If it's the computer's turn, let the computer respond.
                    if (acceptedChallenge?.GetTurnPlayer().Id == computerId)
                    {
                        // Wait for the UI to update
                        await Task.Delay(300);
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
        
        // Redirect to the game page
        return RedirectToAction(nameof(GamesController.GetHtml), "Games", new { id = challengeId });
    }
}