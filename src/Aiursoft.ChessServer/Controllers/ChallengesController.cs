using System.ComponentModel.DataAnnotations;
using Aiursoft.AiurObserver.Extensions;
using Aiursoft.AiurObserver.WebSocket.Server;
using Aiursoft.ChessServer.Attributes;
using Aiursoft.ChessServer.Data;
using Aiursoft.ChessServer.Models;
using Aiursoft.ChessServer.Models.ViewModels;
using Aiursoft.CSTools.Services;
using Aiursoft.WebTools.Attributes;
using Microsoft.AspNetCore.Mvc;

namespace Aiursoft.ChessServer.Controllers;

[Route("challenge")]
public class ChallengesController (
    Counter counter,
    InMemoryDatabase database) : Controller
{
    [Route("")]
    [HttpGet]
    public IActionResult Auto(Guid playerId)
    {
        // I created a challenge. Go to my challenge.
        var myChallengeKey = database.GetMyOpenChallenge(playerId);
        if (myChallengeKey != null)
        {
            return RedirectToAction(nameof(Challenge), new { id = (int)myChallengeKey });
        }
        
        // Exists a public challenge. Go to that challenge.
        var otherChallenge = database.GetFirstPublicChallengeKey();
        if (otherChallenge != null)
        {
            return RedirectToAction(nameof(Challenge), new { id = (int)otherChallenge });
        }
        
        // Create a new challenge.
        return RedirectToAction(nameof(Create), new { playerId });
    }
    
    [HttpGet]
    [Route("create")]
    public IActionResult Create(Guid playerId)
    {
        var myChallengeKey = database.GetMyOpenChallenge(playerId);
        if (myChallengeKey != null)
        {
            // This player already has a challenge. Redirect to that challenge.
            return RedirectToAction(nameof(Challenge), new { id = myChallengeKey, playerId });
        }
        var model = new CreateChallengeViewModel();
        return View(model);
    }

    [HttpPost]
    [Route("create")]
    public IActionResult Create(CreateChallengeViewModel model)
    {
        // Ensure single challenge can be created by a player.
        var myChallengeKey = database.GetMyOpenChallenge(model.CreatorId);
        if (myChallengeKey != null)
        {
            ModelState.AddModelError(nameof(model.CreatorId), "You already have a challenge!");
        }
        if (!ModelState.IsValid)
        {
            return View(model);
        }
        
        // Create a new challenge.
        var player = database.GetOrAddPlayer(model.CreatorId);
        var challenge = new Challenge(
            creator: player, 
            message: model.Message, 
            roleRule: model.RoleRule,
            timeLimit: model.TimeLimit,
            permission: model.Permission);
        var challengeId = counter.GetUniqueNo();
        database.CreateChallenge(challengeId, challenge);
        return RedirectToAction(nameof(Challenge), new { id = challengeId });
    }
    
    [HttpGet]
    [Route("{id:int}")]
    public IActionResult Challenge(int id)
    {
        var challenge = database.GetChallenge(id);
        if (challenge == null)
        {
            // Challenge not found.
            return NotFound();
        }

        if (challenge is AcceptedChallenge)
        {
            // Challenge already accepted.
            return RedirectToAction(nameof(GamesController.GetHtml), "Games", new { id });
        }
        
        var model = new ChallengeViewModel
        {
            ChallengeId = id,
        };
        return View(model);
    }
    
    [HttpPost]
    [Route("{id:int}/drop")]
    public IActionResult Drop([FromRoute][Required]int id, [FromForm][Required][IsGuid]string playerId)
    {
        if (!ModelState.IsValid)
        {
            return RedirectToAction(nameof(Challenge), new { id });
        }
        var challenge = database.GetChallenge(id);
        if (challenge != null && challenge.Creator.Id == Guid.Parse(playerId))
        {
            database.DeleteChallenge(id);
        }

        return RedirectToAction(nameof(Index), "Home");
    }

    [HttpPost]
    [Route("accept/{id:int}")]
    public async Task<IActionResult> Accept([FromRoute]int id, [FromQuery]Guid playerId)
    {
        try
        {
            await database.PatchChallengeAsAcceptedAsync(id, playerId);
        }
        catch (InvalidOperationException e)
        {
            return BadRequest(e.Message);
        }
        return Ok();
    }

    [EnforceWebSocket]
    [Route("listen/{id:int}.ws")]
    public async Task ListenChallenge(int id)
    {
        var pusher = await HttpContext.AcceptWebSocketClient();
        var challenge = database.GetChallenge(id);
        if (challenge == null)
        {
            return;
        }
        var outSub = challenge
            .ChallengeChangedChannel
            .Subscribe(t => pusher.Send(t, HttpContext.RequestAborted));
        try
        {
            await pusher.Listen(HttpContext.RequestAborted);
        }
        catch (TaskCanceledException)
        {
            // Ignore. This happens when the client closes the connection.
        }
        finally
        {
            outSub.Unsubscribe();
            if (pusher.Connected)
            {
                await pusher.Close(HttpContext.RequestAborted);
            }
        }
    }
}