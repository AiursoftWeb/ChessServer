using Aiursoft.ChessServer.Services;
using Aiursoft.ChessServer.Models;
using Microsoft.AspNetCore.Mvc;
using Aiursoft.WebTools.Attributes;

namespace Aiursoft.ChessServer.Controllers;

[Route("pve")]
public class PveController(GameSessionService sessions, LlmModelCatalog catalog) : ChessController
{
    [HttpPost("new"), ValidateAntiForgeryToken, LimitPerMin]
    public async Task<IActionResult> New([FromForm] string difficulty = "1", [FromForm] string? model = null, [FromForm] RoleRule roleRule = RoleRule.CreatorWhite, [FromForm] int minutes = 0, [FromForm] int incrementSeconds = 0)
    {
        if (!ModelState.IsValid) return BadRequest("Choose valid game settings.");
        var llm = difficulty == "llm";
        if (!llm && (!int.TryParse(difficulty, out var number) || number is < 1 or > 8)) return BadRequest("Choose a difficulty from 1 to 8 or LLM.");
        if (llm)
        {
            string[] models;
            try { models = await catalog.GetModelsAsync(HttpContext.RequestAborted); }
            catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException or KeyNotFoundException or InvalidOperationException)
            { return StatusCode(503, "The model list is temporarily unavailable. Please try again."); }
            if (model == null || !models.Contains(model, StringComparer.Ordinal)) return BadRequest("Choose an available model.");
        }
        var level = llm ? (int?)null : int.Parse(difficulty);
        if (!llm) model = null;
        if (!Enum.IsDefined(roleRule) || !new[] { 0, 3, 5, 10, 15 }.Contains(minutes)) return BadRequest("Choose valid pieces and time control.");
        if (!new[] { 0, 1, 2, 3, 5, 10, 15, 30 }.Contains(incrementSeconds) || minutes == 0 && incrementSeconds != 0)
            return BadRequest("Choose a valid time increment.");
        int id;
        try
        {
            id = await sessions.CreateAsync(new Challenge(CurrentPlayer(), "Play against computer", roleRule,
                minutes == 0 ? TimeSpan.MaxValue : TimeSpan.FromMinutes(minutes), ChallengePermission.Public)
            { Difficulty = level, LlmModel = model, IncrementSeconds = incrementSeconds });
            await sessions.AcceptAsync(id, new Player(Guid.NewGuid()) { NickName = llm ? $"LLM — {model}" : $"Computer — level {level}" });
        }
        catch (InvalidOperationException e) { return BadRequest(e.Message); }
        return RedirectToAction(nameof(GamesController.GetHtml), "Games", new { id });
    }
}
