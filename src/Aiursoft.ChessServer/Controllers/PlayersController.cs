using System.ComponentModel.DataAnnotations;
using Aiursoft.ChessServer.Attributes;
using Aiursoft.ChessServer.Data;
using Microsoft.AspNetCore.Mvc;

namespace Aiursoft.ChessServer.Controllers;

[Route("players")]
public class PlayersController(InMemoryDatabase database) : ControllerBase
{
    [Route("{id:guid}")]
    public IActionResult Me([Required]Guid id)
    {
        var me = database.GetOrAddPlayer(id);
        return Ok(me);
    }
    
    [HttpPut]
    [Route("{id:guid}/new-name/{nickname}")]
    public IActionResult ChangeNickname([Required]Guid id, [Required][MaxLength(20)][ValidNickName]string nickname)
    {
        if (ModelState.IsValid == false)
        {
            return BadRequest();
        }
        
        var me = database.GetOrAddPlayer(id);
        me.NickName = nickname;
        return Ok();
    }
}