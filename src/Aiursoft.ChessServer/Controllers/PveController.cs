using Aiursoft.AiurObserver;
using Aiursoft.AiurObserver.WebSocket;
using Aiursoft.ChessServer.Data;
using Aiursoft.ChessServer.Models;
using Aiursoft.CSTools.Services;
using Microsoft.AspNetCore.Mvc;
using Aiursoft.AiurObserver.Extensions;
using Aiursoft.ChessServer.Services;
using Chess;

namespace Aiursoft.ChessServer.Controllers;

[Route("pve")]
public class PveController(
    ILogger<PveController> logger,
    ChessEngine engine,
    Counter counter,
    InMemoryDatabase database) : Controller
{
    [Route("new")]
    [HttpGet]
    public async Task<IActionResult> New(Guid playerId, int difficulty = 1)
    {
        if (difficulty > 15) difficulty = 15;
        
        // Add a computer player
        logger.LogInformation("Creating a new PVE game for player {playerId}.", playerId);
        var computerId = Guid.NewGuid();
        database.GetOrAddPlayer(computerId).NickName = engine.GetComputerName(difficulty) + " AI";
        //var asyncLock = new SemaphoreSlim(1, 1);

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
        
        // Let computer respond to the player's move
        await Task.Factory.StartNew(async () =>
        {
            ISubscription? subscription = null;
            try
            {
                var webSocketSchema = HttpContext.Request.Scheme == "https" ? "wss" : "ws";
                var webSocketEndpoint =
                    $"{webSocketSchema}://{HttpContext.Request.Host}/games/{challengeId}.ws?playerId={computerId}";
                
                // TODO: Refactor required. Currently, PVE, the computer is also a WebSocket client connecting to localhost.
                // This is not a good practice. The computer should be a WebSocket server that listens to the game's WebSocket.
                // Because currently, if the user dropped, the computer will not know and will keep calculating the best move.
                var client = await webSocketEndpoint.ConnectAsWebSocketServer();
                subscription = client.Subscribe(async fen =>
                {
                    logger.LogInformation("Computer player received fen: {fen}", fen);
                    
                    // When fen changes, it means someone has made a move. If it's the computer's turn, let the computer respond.
                    if (ChessBoard.LoadFromFen(fen).Turn == PieceColor.Black)
                    {
                        logger.LogInformation("The fen {fen} means it's the computer's turn. Computer is calculating the best move.", fen);
                        // Wait for the UI to update
                        await Task.Delay(300);
                        var bestMove = engine.GetBestMove(fen, difficulty);
                        
                        logger.LogInformation("Computer calculated the best move: {bestMove}", bestMove);
                        await client.Send(bestMove);
                    }
                });
                await client.Listen();
            }
            finally
            {
                subscription?.Unsubscribe();
                logger.LogInformation("Computer player unsubscribed.");
            }
        });
        
        // Redirect to the game page
        return RedirectToAction(nameof(GamesController.GetHtml), "Games", new { id = challengeId });
    }
}