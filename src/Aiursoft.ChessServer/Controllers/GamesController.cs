using AiurObserver;
using Aiursoft.ChessServer.Data;
using Aiursoft.ChessServer.Models;
using Aiursoft.ChessServer.Services;
using Microsoft.AspNetCore.Mvc;

namespace Aiursoft.ChessServer.Controllers;

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

    [Route("games")]
    public IActionResult GetAll()
    {
        var games = _database.GetActiveGames();
        return Ok(games);
    }

    [Route("games/{id}")]
    public IActionResult GetInfo([FromRoute] int id)
    {
        var game = _database.GetOrAddBoard(id);
        return Ok(new
        {
            Turn = game.Turn.AsChar,
            Ended = game.IsEndGame,
            End = game.EndGame?.EndgameType,
            Won = game.EndGame?.WonSide,
            game.MoveIndex,
            game.WhiteKingChecked,
            game.BlackKingChecked,
            links = new Dictionary<string, string>
            {
                { "ascii", $"games/{id}/ascii"},
                { "fen", $"games/{id}/fen"},
                { "pgn", $"games/{id}/pgn"},
                { "html", $"games/{id}/html"},
                {"websocket", $"games/{id}/websocket"},
                { "move-post", $"games/{id}/move/{{player}}/{{move_algebraic_notation}}"}
            },
            Listeners = _database.GetOrAddChannel(id).GetListenerCount()
        });
    }
    
    [Route("games/{id}/ws")]
    public async Task GetWebSocket([FromRoute] int id)
    {
        var channel= _database.GetOrAddChannel(id);
        IDisposable? subscription = null;
        
        await _pusher.Accept(HttpContext);
        try
        {
            await Task.Factory.StartNew(_pusher.PendingClose);
            subscription = channel.Subscribe(async t =>
            {
                await _pusher.SendMessage(t.Content);
            });
            while (_pusher.Connected)
            {
                await Task.Delay(int.MaxValue, HttpContext.RequestAborted);
            }
        }
        finally
        {
            await _pusher.Close();
            subscription?.Dispose();
        }
    }

    [Route("games/{id}/ascii")]
    public IActionResult GetAscii([FromRoute] int id)
    {
        var game = _database.GetOrAddBoard(id);
        return Ok(game.ToAscii());
    }
    
    [Route("games/{id}/html")]
    public IActionResult GetHtml([FromRoute] int id)
    {
        return View(id);
    }

    [Route("games/{id}/fen")]
    public IActionResult GetFen([FromRoute] int id)
    {
        var game = _database.GetOrAddBoard(id);
        return Ok(game.ToFen());
    }

    [Route("games/{id}/pgn")]
    public IActionResult GetPgn([FromRoute]int id)
    {
        var game = _database.GetOrAddBoard(id);
        return Ok(game.ToPgn());
    }

    [HttpPost]
    [Route("games/{id}/move/{player}/{move}")]
    public async Task<IActionResult> Move([FromRoute]int id, [FromRoute]string player, [FromRoute]string move)
    {
        var game = _database.GetOrAddBoard(id);
        try
        {
            if (game.IsValidMove(move) && !game.IsEndGame && game.Turn.AsChar.ToString() == player)
            {
                game.Move(move);
                var fen = game.ToFen();
                var channel = _database.GetOrAddChannel(id);
                await channel.Push(new Message(fen));
                return Ok(fen);
            }
            return BadRequest();
        }
        catch
        {
            return BadRequest();
        }
    }
}
