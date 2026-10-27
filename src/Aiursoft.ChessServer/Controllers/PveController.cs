using Aiursoft.ChessServer.Data;
using Aiursoft.ChessServer.Models;
using Microsoft.AspNetCore.Mvc;

namespace Aiursoft.ChessServer.Controllers;

[Route("pve")]
public class PveController(InMemoryDatabase database) : ChessController
{
    [HttpPost("new"), ValidateAntiForgeryToken]
    public async Task<IActionResult> New([FromForm] int difficulty = 1)
    {
        if (difficulty is < 1 or > 8) return BadRequest("Choose a difficulty from 1 to 8.");
        int id;
        try
        {
            id = database.CreateChallenge(new Challenge(CurrentPlayer(), "Play against computer", RoleRule.CreatorWhite,
                TimeSpan.MaxValue, ChallengePermission.Public)
            { Difficulty = difficulty });
            await database.AcceptAsync(id, new Player(Guid.NewGuid()) { NickName = $"Computer — level {difficulty}" });
        }
        catch (InvalidOperationException e) { return BadRequest(e.Message); }
        return RedirectToAction(nameof(GamesController.GetHtml), "Games", new { id });
    }
}
