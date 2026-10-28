using Aiursoft.ChessServer.Entities;
using Aiursoft.ChessServer.Models.ViewModels;
using Aiursoft.ChessServer.Services;
using Aiursoft.UiStack.Navigation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.ChessServer.Controllers;

[Authorize]
public class HistoryController(TemplateDbContext context) : ChessController
{
    [RenderInNavBar(NavGroupName = "Features", NavGroupOrder = 1, CascadedLinksGroupName = "Chess",
        CascadedLinksIcon = "crown", CascadedLinksOrder = 1, LinkText = "My games", LinkOrder = 2)]
    public async Task<IActionResult> Index(DateOnly? date, int offsetMinutes = 0, int page = 1)
    {
        if (!ModelState.IsValid || offsetMinutes is < -840 or > 840 || page is < 1 or > 100000) return BadRequest();
        var day = date ?? DateOnly.FromDateTime(DateTime.UtcNow.AddMinutes(-offsetMinutes));
        if (day.Year is < 2000 or > 9998) return BadRequest();
        var start = day.ToDateTime(TimeOnly.MinValue).AddMinutes(offsetMinutes);
        var end = start.AddDays(1);
        var user = PlayerId.ToString();
        var games = await context.CompletedGames.AsNoTracking()
            .Where(g => (g.WhiteUserId == user || g.BlackUserId == user) && g.FinishedAt >= start && g.FinishedAt < end)
            .OrderByDescending(g => g.FinishedAt).ThenBy(g => g.Id).Skip((page - 1) * 20).Take(21).ToListAsync();
        return this.StackView(new HistoryViewModel { Date = day, OffsetMinutes = offsetMinutes, Page = page, HasMore = games.Count > 20, Games = games.Take(20).ToList() });
    }

    public async Task<IActionResult> Replay(Guid id)
    {
        var game = await FindMine(id);
        return game == null ? NotFound() : this.StackView(new ReplayViewModel { Game = game, IsBlack = game.BlackUserId == PlayerId.ToString() });
    }

    public async Task<IActionResult> Pgn(Guid id)
    {
        var game = await FindMine(id);
        return game == null ? NotFound() : File(System.Text.Encoding.UTF8.GetBytes(game.Pgn), "application/x-chess-pgn", $"{game.Id}.pgn");
    }

    private Task<CompletedGame?> FindMine(Guid id)
    {
        var user = PlayerId.ToString();
        return context.CompletedGames.AsNoTracking().SingleOrDefaultAsync(g => g.Id == id && (g.WhiteUserId == user || g.BlackUserId == user));
    }
}
