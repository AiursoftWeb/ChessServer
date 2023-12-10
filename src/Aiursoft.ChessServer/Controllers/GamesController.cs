using AiurObserver;
using Aiursoft.ChessServer.Data;
using Aiursoft.ChessServer.Services;
using Microsoft.AspNetCore.Mvc;

namespace Aiursoft.ChessServer.Controllers;

[Route("games")]
public class GamesController : Controller
{
    private readonly WebSocketPusher _pusher;
    private readonly InMemoryDatabase _database;

    public GamesController(
        WebSocketPusher pusher,
        InMemoryDatabase database)
    {
        _pusher = pusher;
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
    public async Task GetWebSocket([FromRoute] int id)
    {
        var game = _database.GetOrAddGame(id);
        await _pusher.Accept(HttpContext);
        var subscription = game.Channel.Subscribe(_pusher.Send);
        try
        {
            await _pusher.Wait();
        }
        finally
        {
            await _pusher.Close();
            subscription!.UnRegister();
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

    [HttpPost]
    [Route("{id:int}/move/{player}/{move}")]
    public async Task<IActionResult> Move([FromRoute] int id, [FromRoute] string player, [FromRoute] string move)
    {
        var game = _database.GetOrAddGame(id);
        try
        {
            if (!game.Board.IsValidMove(move) || game.Board.IsEndGame || game.Board.Turn.AsChar.ToString() != player)
            {
                return BadRequest();
            }

            game.Board.Move(move);
            var fen = game.Board.ToFen();
            await Task.WhenAll(game.Channel.Broadcast(fen));
            return Ok(fen);
        }
        catch
        {
            return BadRequest();
        }
    }
}