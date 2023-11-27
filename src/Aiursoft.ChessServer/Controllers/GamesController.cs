using Aiursoft.ChessServer.Data;
using Aiursoft.ChessServer.Models;
using Aiursoft.ChessServer.Services;
using Aiursoft.CSTools.Services;
using Microsoft.AspNetCore.Mvc;

namespace Aiursoft.ChessServer.Controllers;

public class GamesController : Controller
{
    private readonly Counter _counter;
    private readonly WebSocketPusher _pusher;
    private readonly InMemoryDatabase _database;

    public GamesController(
        Counter counter,
        WebSocketPusher pusher,
        InMemoryDatabase database)
    {
        _counter = counter;
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
        var game = _database.GetOrAdd(id);
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
            }
        });
    }
    
    [Route("games/{id}/ws")]
    public async Task<IActionResult> GetWebSocket([FromRoute] int id)
    {
        var lastReadId = _counter.GetCurrent;
        var (channel, blocker) = _database.ListenChannel(id);
        
        await _pusher.Accept(HttpContext);
        try
        {
            await Task.Factory.StartNew(_pusher.PendingClose);
            while (_pusher.Connected)
            {
                await blocker.WaitAsync();
                var nextMessages = channel
                    .GetMessagesFrom(lastReadId)
                    .ToList();
                var messageToPush = nextMessages.MinBy(t => t.Id);
                await _pusher.SendMessage(messageToPush!.Content);
                lastReadId = messageToPush.Id;
            }
        }
        finally
        {
            await _pusher.Close();
            if (!channel.UnRegister(out blocker))
            { 
                throw new InvalidOperationException("Failed to unregister blocker!");
            }
        }

        return NoContent();
    }

    [Route("games/{id}/ascii")]
    public IActionResult GetAscii([FromRoute] int id)
    {
        var game = _database.GetOrAdd(id);
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
        var game = _database.GetOrAdd(id);
        return Ok(game.ToFen());
    }

    [Route("games/{id}/pgn")]
    public IActionResult GetPgn([FromRoute]int id)
    {
        var game = _database.GetOrAdd(id);
        return Ok(game.ToPgn());
    }

    [HttpPost]
    [Route("games/{id}/move/{player}/{move}")]
    public IActionResult Move([FromRoute]int id, [FromRoute]string player, [FromRoute]string move)
    {
        var game = _database.GetOrAdd(id);
        try
        {
            if (game.IsValidMove(move) && !game.IsEndGame && game.Turn.AsChar.ToString() == player)
            {
                game.Move(move);
                var fen = game.ToFen();
                if (_database.Channels.TryGetValue(id, out var channel))
                {
                    channel.Push(new Message(fen)
                    {
                        Id = _counter.GetUniqueNo(),
                    });
                }
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
