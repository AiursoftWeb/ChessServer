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
    public IActionResult ChangeNickname([Required]Guid id, [Required][MaxLength(40)][ValidNickName]string nickname)
    {
        if (ModelState.IsValid == false)
        {
            return BadRequest("Only numbers, alphabet and underline are allowed in nickname. Max length is 40.");
        }
        
        var me = database.GetOrAddPlayer(id);
        me.NickName = nickname;
        return Ok();
    }
}