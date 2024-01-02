using Microsoft.AspNetCore.Mvc;

namespace Aiursoft.ChessServer.Controllers;

[Route("challenges")]
public class ChallengeController : ControllerBase
{
    [HttpPost]
    [Route("create")]
    public IActionResult Create(Guid userId)
    {
        // Create a new challenge. Add to database.
        return Ok();
    }

    [HttpGet]
    [Route("list")]
    public IActionResult List()
    {
        // Show all public challenges.
        return Ok();
    }
    
    [HttpPost]
    [Route("accept/{id:int}")]
    public IActionResult Accept(int id, Guid userId)
    {
        // Accept a challenge. Remove the challenge from database.
        // Create a new game. Redirect both players to the game.
        return Ok();
    }
    
    [HttpPost]
    [Route("reject/{id:int}")]
    public IActionResult Edit(int id, Guid userId)
    {
        // If challenge owner is the same as user id,
        // Edit a challenge in database.
        return Ok();
    }
}