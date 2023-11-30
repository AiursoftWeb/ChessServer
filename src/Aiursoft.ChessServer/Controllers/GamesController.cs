using AiurObserver;
using Aiursoft.ChessServer.Data;
using Aiursoft.ChessServer.Models;
using Aiursoft.ChessServer.Services;
using Microsoft.AspNetCore.Mvc;

namespace Aiursoft.ChessServer.Controllers;

[Route("games")]
public class GamesController : Controller
{
    private readonly ILogger<GamesController> _logger;
    private readonly WebSocketPusher _pusher;
    private readonly InMemoryDatabase _database;

    public GamesController(
        ILogger<GamesController> logger,
        WebSocketPusher pusher,
        InMemoryDatabase database)
    {
        _logger = logger;
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
        IDisposable? subscription = null;

        await _pusher.Accept(HttpContext);
        try
        {
            subscription = game.Channel.Subscribe(async t => 
            {
                await _pusher.SendMessage(t.Content); 
                _logger.LogInformation("Message was sent to client!");
            });

            _logger.LogInformation("Game {Id} registering events done. Waiting for close. Now it has {Count} subscribers", id, game.Channel.GetListenerCount());
            await _pusher.PendingClose();
        }
        finally
        {
            await _pusher.Close();
            subscription?.Dispose();
            _logger.LogInformation("Getting game {Id} websocket connection was interrupted. Now it has {Count} subscribers", id, game.Channel.GetListenerCount());
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
            await game.Channel.Push(new Message(fen));
            return Ok(fen);
        }
        catch
        {
            return BadRequest();
        }
    }
}