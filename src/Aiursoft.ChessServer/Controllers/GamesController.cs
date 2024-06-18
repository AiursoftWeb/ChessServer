using Aiursoft.AiurObserver;
using Aiursoft.AiurObserver.WebSocket.Server;
using Aiursoft.ChessServer.Data;
using Aiursoft.ChessServer.Models.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Aiursoft.AiurObserver.Extensions;
using Aiursoft.WebTools.Attributes;

namespace Aiursoft.ChessServer.Controllers;

[Route("games")]
public class GamesController(InMemoryDatabase database) : Controller
{
    [Route("{id:int}.json")]
    public IActionResult GetInfo([FromRoute] int id)
    {
        var challenge = database.GetAcceptedChallenge(id);
        if (challenge == null)
        {
            return NotFound();
        }

        return Ok(new ChallengeContext(challenge, id));
    }

    [Route("{id:int}.color")]
    public IActionResult GetColor([FromRoute] int id, [FromQuery] string playerId)
    {
        var validId = Guid.TryParse(playerId, out var playerGuid);
        if (!validId)
        {
            return NotFound();
        }
        var challenge = database.GetAcceptedChallenge(id);
        if (challenge == null)
        {
            return NotFound();
        }
        
        return Ok(challenge.GetPlayerColor(playerGuid));
    }

    [Route("{id:int}.ws")]
    [EnforceWebSocket]
    public async Task GetWebSocket([FromRoute] int id, [FromQuery] string playerId)
    {
        var validId = Guid.TryParse(playerId, out var playerGuid);
        if (!validId)
        {
            return;
        }
        var challenge = database.GetAcceptedChallenge(id);
        if (challenge == null)
        {
            return;
        }

        var game = challenge.Game;
        var pusher = await HttpContext.AcceptWebSocketClient();
        var outSub = game
            .FenChangedChannel
            .Subscribe(t => pusher.Send(t, HttpContext.RequestAborted));

        var inSub = pusher
            .Filter(t => !string.IsNullOrWhiteSpace(t))
            .Subscribe(async move =>
            {
                lock (game.MovePieceLock)
                {
                    if (!game.Board.IsEndGame &&
                        game.Board.IsValidMove(move) &&
                        challenge.GetTurnPlayer().Id == playerGuid)
                    {
                        game.Board.Move(move);
                    }
                }

                await game.FenChangedChannel.BroadcastAsync(game.Board.ToFen());
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

    [Route("{id:int}.ascii")]
    public IActionResult GetAscii([FromRoute] int id)
    {
        var game = database.GetAcceptedChallenge(id)?.Game;
        if (game == null)
        {
            return NotFound();
        }

        return Ok(game.Board.ToAscii());
    }

    [Route("{id:int}.html")]
    public IActionResult GetHtml([FromRoute] int id)
    {
        var game = database.GetAcceptedChallenge(id)?.Game;
        if (game == null)
        {
            return NotFound();
        }

        return View(id);
    }

    [Route("{id:int}.fen")]
    public IActionResult GetFen([FromRoute] int id)
    {
        var game = database.GetAcceptedChallenge(id)?.Game;
        if (game == null)
        {
            return NotFound();
        }

        return Ok(game.Board.ToFen());
    }

    [Route("{id:int}.pgn")]
    public IActionResult GetPgn([FromRoute] int id)
    {
        var game = database.GetAcceptedChallenge(id)?.Game;
        if (game == null)
        {
            return NotFound();
        }

        return Ok(game.Board.ToPgn());
    }
}