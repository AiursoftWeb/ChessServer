using Aiursoft.AiurObserver.Extensions;
using Aiursoft.AiurObserver.WebSocket.Server;
using Aiursoft.ChessServer.Data;
using Aiursoft.ChessServer.Models;
using Aiursoft.ChessServer.Models.ViewModels;
using Aiursoft.CSTools.Services;
using Aiursoft.WebTools.Attributes;
using Microsoft.AspNetCore.Mvc;

namespace Aiursoft.ChessServer.Controllers;

public class HomeController(
    Counter counter,
    InMemoryDatabase database) : Controller
{
    [HttpGet]
    public IActionResult Index()
    {
        var model = new IndexViewModel
        {
            Challenges = database.GetPublicUnAcceptedChallenges()
        };
        return View(model);
    }

    /// <summary>
    /// This action will auto redirect to a challenge.
    ///
    /// If the player has a challenge, then go to that challenge.
    ///
    /// If not, then go to a public challenge. If not, then create a new challenge.
    /// </summary>
    /// <param name="playerId"></param>
    /// <returns></returns>
    [HttpGet]
    public IActionResult Auto(Guid playerId)
    {
        // I created a challenge. Go to my challenge.
        var myChallengeKey = database.GetMyChallengeKey(playerId);
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
    
    /// <summary>
    /// This action renders a page to create a new challenge.
    ///
    /// However, if the player has a challenge, then redirect to that challenge.
    /// </summary>
    /// <param name="playerId"></param>
    /// <returns></returns>
    [HttpGet]
    public IActionResult Create(Guid playerId)
    {
        var myChallengeKey = database.GetMyChallengeKey(playerId);
        if (myChallengeKey != null)
        {
            return RedirectToAction(nameof(Challenge), new { id = myChallengeKey, playerId });
        }
        var model = new CreateChallengeViewModel();
        return View(model);
    }

    [HttpPost]
    public IActionResult Create(CreateChallengeViewModel model)
    {
        // Ensure single challenge can be created by a player.
        var myChallengeKey = database.GetMyChallengeKey(model.CreatorId);
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
        var challenge = new Challenge(player)
        {
            RoleRule = model.RoleRule,
            Message = model.Message,
            Permission = model.Permission,
            TimeLimit = model.TimeLimit,
        };
        var challengeId = counter.GetUniqueNo();
        database.CreateChallenge(challengeId, challenge);
        return RedirectToAction(nameof(Challenge), new { id = challengeId });
    }
    
    /// <summary>
    /// This action renders a page to show the details of a challenge.
    ///
    /// Will use JavaScript to call accept challenge API.
    ///
    /// Will use WebSocket to listen to the challenge changes.
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    [HttpGet]
    public IActionResult Challenge(int id)
    {
        var challenge = database.GetChallenge(id);
        if (challenge == null)
        {
            // Challenge not found.
            return NotFound();
        }
        var model = new ChallengeViewModel
        {
            ChallengeId = id,
        };
        return View(model);
    }
    
    [HttpPost]
    public IActionResult DropChallenge(DropChallengeViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return RedirectToAction(nameof(Challenge), new { id = model.Id, playerId = model.PlayerId });
        }
        var challenge = database.GetChallenge(model.Id);
        if (challenge != null && challenge.Creator.Id == model.PlayerId)
        {
            database.DeleteChallenge(model.Id);
        }
        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// This method accepts a challenge and updates the challenge's accepter.
    /// </summary>
    /// <param name="id">The ID of the challenge to accept.</param>
    /// <param name="playerId">The ID of the player accepting the challenge.</param>
    /// <returns>An IActionResult indicating the result of the operation.</returns>
    [HttpPost]
    public async Task<IActionResult> AcceptChallenge([FromRoute]int id, [FromQuery]Guid playerId)
    {
        var challenge = database.GetChallenge(id);
        if (challenge == null)
        {
            return NotFound();
        }
        if (challenge.Accepter != null)
        {
            // Challenge already accepted.
            return BadRequest("Challenge already accepted.");
        }
        if (challenge.Creator.Id == playerId)
        {
            // Cannot accept your own challenge.
            return BadRequest("Cannot accept your own challenge!");
        }
        challenge.Accepter = database.GetOrAddPlayer(playerId);
        var (newGameId, newGame) = database.AddNewGameAndGetId();
        challenge.Game = newGame;
        challenge.GameId = newGameId;
        await challenge.ChallengeChangedChannel.BroadcastAsync("game-started-at-" + newGameId);
        return Ok();
    }

    [Route("listen-challenge/{id:int}.ws")]
    [EnforceWebSocket]
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
            await pusher.Close(HttpContext.RequestAborted);
            outSub.Unsubscribe();
        }
    }
}