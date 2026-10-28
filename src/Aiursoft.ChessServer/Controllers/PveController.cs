using Aiursoft.ChessServer.Services;
using Aiursoft.ChessServer.Models;
using Microsoft.AspNetCore.Mvc;

namespace Aiursoft.ChessServer.Controllers;

[Route("pve")]
public class PveController(GameSessionService sessions) : ChessController
{
    [HttpPost("new"), ValidateAntiForgeryToken]
    public async Task<IActionResult> New([FromForm] int difficulty = 1, [FromForm] RoleRule roleRule = RoleRule.CreatorWhite, [FromForm] int minutes = 0, [FromForm] int incrementSeconds = 0)
    {
        if (!ModelState.IsValid) return BadRequest("Choose valid game settings.");
        if (difficulty is < 1 or > 8) return BadRequest("Choose a difficulty from 1 to 8.");
        if (!Enum.IsDefined(roleRule) || !new[] { 0, 3, 5, 10, 15 }.Contains(minutes)) return BadRequest("Choose valid pieces and time control.");
        if (!new[] { 0, 1, 2, 3, 5, 10, 15, 30 }.Contains(incrementSeconds) || minutes == 0 && incrementSeconds != 0)
            return BadRequest("Choose a valid time increment.");
        int id;
        try
        {
            id = await sessions.CreateAsync(new Challenge(CurrentPlayer(), "Play against computer", roleRule,
                minutes == 0 ? TimeSpan.MaxValue : TimeSpan.FromMinutes(minutes), ChallengePermission.Public)
            { Difficulty = difficulty, IncrementSeconds = incrementSeconds });
            await sessions.AcceptAsync(id, new Player(Guid.NewGuid()) { NickName = $"Computer — level {difficulty}" });
        }
        catch (InvalidOperationException e) { return BadRequest(e.Message); }
        return RedirectToAction(nameof(GamesController.GetHtml), "Games", new { id });
    }
}
