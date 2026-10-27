using Aiursoft.AiurObserver;
using Aiursoft.AiurObserver.WebSocket.Server;
using Aiursoft.ChessServer.Data;
using Aiursoft.ChessServer.Models;
using Aiursoft.ChessServer.Models.ViewModels;
using Aiursoft.ChessServer.Services;
using Aiursoft.WebTools.Attributes;
using Microsoft.AspNetCore.Mvc;

namespace Aiursoft.ChessServer.Controllers;

[Route("challenge")]
public class ChallengesController(InMemoryDatabase database) : ChessController
{
    [HttpGet("")]
    public IActionResult Auto()
    {
        var id = database.GetMyOpenChallenge(PlayerId) ?? database.GetPublicOpenChallenges().Select(p => (int?)p.Key).FirstOrDefault();
        return id.HasValue ? RedirectToAction(nameof(Challenge), new { id }) : RedirectToAction(nameof(Create));
    }

    [HttpGet("create")]
    public IActionResult Create() => this.StackView(new CreateChallengeViewModel());

    [HttpPost("create"), ValidateAntiForgeryToken]
    public IActionResult Create(CreateChallengeViewModel model)
    {
        if (!ModelState.IsValid) return this.StackView(model);
        try
        {
            var id = database.CreateChallenge(new Challenge(CurrentPlayer(), model.Message, model.RoleRule,
                TimeSpan.FromMinutes(model.Minutes), model.Permission));
            return RedirectToAction(nameof(Challenge), new { id });
        }
        catch (InvalidOperationException e)
        {
            ModelState.AddModelError(string.Empty, e.Message);
            return this.StackView(model);
        }
    }

    [HttpGet("{id:int}")]
    public IActionResult Challenge(int id)
    {
        var room = database.GetChallenge(id);
        if (room == null) return NotFound();
        if (room is AcceptedChallenge) return RedirectToAction(nameof(GamesController.GetHtml), "Games", new { id });
        return this.StackView(new RoomViewModel { Id = id, IsCreator = room.Creator.Id == PlayerId, Message = room.Message });
    }

    [HttpPost("{id:int}/drop"), ValidateAntiForgeryToken]
    public IActionResult Drop(int id)
    {
        var room = database.GetChallenge(id);
        if (room == null) return NotFound();
        if (room.Creator.Id != PlayerId) return Forbid();
        database.DeleteChallenge(id);
        return RedirectToAction("Index", "Lobby");
    }

    [HttpPost("accept/{id:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Accept(int id)
    {
        try { await database.AcceptAsync(id, CurrentPlayer()); }
        catch (InvalidOperationException e) { return BadRequest(e.Message); }
        return RedirectToAction(nameof(GamesController.GetHtml), "Games", new { id });
    }

    [HttpGet("listen/{id:int}.ws"), EnforceWebSocket]
    public async Task ListenChallenge(int id)
    {
        var room = database.GetChallenge(id);
        if (room == null) { Response.StatusCode = 404; return; }
        if (!IsSameOriginWebSocket()) { Response.StatusCode = 403; return; }
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(HttpContext.RequestAborted, room.Lifetime);
        var token = lifetime.Token;
        var client = await HttpContext.AcceptWebSocketClient();
        var subscription = room.ChallengeChangedChannel.Subscribe(t => client.Send(t, token));
        try
        {
            if (database.GetAcceptedChallenge(id) != null) await client.Send("game-started", token);
            await client.Listen(token);
        }
        finally { subscription.Unsubscribe(); await client.Close(CancellationToken.None); }
    }
}
