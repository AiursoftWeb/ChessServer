using Aiursoft.ChessServer.Data;
using Microsoft.AspNetCore.Mvc;

namespace Aiursoft.ChessServer.Controllers;

public class GamesController : ControllerBase
{
    private readonly InMemoryDatabase _database;

    public GamesController(InMemoryDatabase database)
    {
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
                { "ascii", $"game/{id}/ascii"},
                { "fen", $"game/{id}/fen"},
                { "pgn", $"game/{id}/pgn"},
                { "move", $"game/{id}/move"}
            }
        });
    }

    [Route("games/{id}/ascii")]
    public IActionResult GetAscii([FromRoute] int id)
    {
        var game = _database.GetOrAdd(id);
        return Ok(game.ToAscii());
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
    [Route("games/{id}/move")]
    public IActionResult Move([FromRoute]int id, [FromQuery]string move)
    {
        var game = _database.GetOrAdd(id);
        try
        {
            if (game.IsValidMove(move) && !game.IsEndGame)
            {
                game.Move(move);
                return Ok();
            }
            return BadRequest();
        }
        catch
        {
            return BadRequest();
        }
    }
}
