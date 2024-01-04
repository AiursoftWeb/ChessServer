using Aiursoft.AiurObserver;
using Aiursoft.ChessServer.Data;
using Aiursoft.ChessServer.Models;
using Aiursoft.WebTools.Services;
using Microsoft.AspNetCore.Mvc;

namespace Aiursoft.ChessServer.Controllers;

[Route("games")]
public class GamesController : Controller
{
    private readonly InMemoryDatabase _database;

    public GamesController(
        InMemoryDatabase database)
    {
        _database = database;
    }

    [Route("")]
    public IActionResult GetAll()
    {
        var games = _database.GetActiveGames();
        return Ok(games);
    }

    [Route("{id:int}.json")]
    public IActionResult GetInfo([FromRoute] int id)
    {
        var game = _database.GetOrAddGame(id);
        return Ok(new GameContext(game, id));
    }

    [Route("{id:int}.ws")]
    public async Task GetWebSocket([FromRoute] int id, [FromQuery]string player)
    {
        var pusher = await HttpContext.AcceptWebSocketClient();
        var game = _database.GetOrAddGame(id);
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
                    game.Board.Turn.AsChar.ToString() == player)
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
        var game = _database.GetOrAddGame(id);
        return Ok(game.Board.ToAscii());
    }

    [Route("{id:int}.html")]
    public IActionResult GetHtml([FromRoute] int id)
    {
        return View(id);
    }

    [Route("{id:int}.fen")]
    public IActionResult GetFen([FromRoute] int id)
    {
        var game = _database.GetOrAddGame(id);
        return Ok(game.Board.ToFen());
    }

    [Route("{id:int}.pgn")]
    public IActionResult GetPgn([FromRoute] int id)
    {
        var game = _database.GetOrAddGame(id);
        return Ok(game.Board.ToPgn());
    }
}