using Aiursoft.AiurObserver;
using Aiursoft.AiurObserver.WebSocket.Server;
using Aiursoft.ChessServer.Data;
using Aiursoft.ChessServer.Models.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Aiursoft.AiurObserver.Extensions;
using Aiursoft.ChessServer.Attributes;
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
    public IActionResult GetColor([FromRoute] int id, [FromQuery][IsGuid] string playerId)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }
        
        var playerGuid = Guid.Parse(playerId);
        var challenge = database.GetAcceptedChallenge(id);
        if (challenge == null)
        {
            return NotFound();
        }
        
        return Ok(challenge.GetPlayerColor(playerGuid));
    }

    [Route("{id:int}.ws")]
    [EnforceWebSocket]
    public async Task GetWebSocket([FromRoute] int id, [FromQuery][IsGuid] string playerId)
    {
        if (!ModelState.IsValid)
        {
            return;
        }
        var playerGuid = Guid.Parse(playerId);
        var challenge = database.GetAcceptedChallenge(id);
        if (challenge == null)
        {
            return;
        }

        var pusher = await HttpContext.AcceptWebSocketClient();
        var outSub = challenge.Game
            .FenChangedChannel
            .Subscribe(t => pusher.Send(t, HttpContext.RequestAborted));

        var inSub = pusher
            .Filter(t => !string.IsNullOrWhiteSpace(t))
            .Subscribe(async move =>
            {
                move = move.Replace("0", "O");
                lock (challenge.Game.MovePieceLock)
                {
                    if (!challenge.Game.Board.IsEndGame &&
                        challenge.Game.Board.IsValidMove(move) &&
                        challenge.GetTurnPlayer().Id == playerGuid)
                    {
                        challenge.Game.Board.Move(move);
                    }
                }

                await challenge.Game.FenChangedChannel.BroadcastAsync(challenge.Game.Board.ToFen());
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
            outSub.Unsubscribe();
            inSub.Unsubscribe();
            if (pusher.Connected)
            {
                await pusher.Close(HttpContext.RequestAborted);
            }
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