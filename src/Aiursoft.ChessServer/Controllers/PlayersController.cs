using System.ComponentModel.DataAnnotations;
using Aiursoft.ChessServer.Data;
using Microsoft.AspNetCore.Mvc;

namespace Aiursoft.ChessServer.Controllers;

[Route("players")]
public class PlayersController : ControllerBase
{
    private readonly InMemoryDatabase _database;

    public PlayersController(InMemoryDatabase database)
    {
        _database = database;
    }
    
    [Route("{id:guid}")]
    public IActionResult Me([Required]Guid id)
    {
        var me = _database.GetOrAddPlayer(id);
        return Ok(me);
    }
    
    [HttpPut]
    [Route("{id:guid}/name/{nickname}")]
    public IActionResult ChangeNickname([Required]Guid id, [Required]string nickname)
    {
        var me = _database.GetOrAddPlayer(id);
        me.NickName = nickname;
        return Ok(me);
    }
}