using Aiursoft.AiurObserver;
using Aiursoft.ChessServer.Data;
using Aiursoft.ChessServer.Models;
using Aiursoft.ChessServer.Models.ViewModels;
using Aiursoft.CSTools.Services;
using Aiursoft.WebTools.Services;
using Microsoft.AspNetCore.Mvc;

namespace Aiursoft.ChessServer.Controllers;

public class HomeController : Controller
{
    private readonly Counter _counter;
    private readonly InMemoryDatabase _database;

    public HomeController(
        Counter counter,
        InMemoryDatabase database)
    {
        _counter = counter;
        _database = database;
    }

    [HttpGet]
    public IActionResult Index()
    {
        var model = new IndexViewModel
        {
            Challenges = _database.GetPublicChallenges()
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
        var myChallengeKey = _database.GetMyChallengeKey(playerId);
        if (myChallengeKey != null)
        {
            return RedirectToAction(nameof(Challenge), new { id = (int)myChallengeKey, playerId });
        }
        
        // Exists a public challenge. Go to that challenge.
        var otherChallenge = _database.GetFirstPublicChallengeKey();
        if (otherChallenge != null)
        {
            return RedirectToAction(nameof(Challenge), new { id = (int)otherChallenge, playerId });
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
        var myChallengeKey = _database.GetMyChallengeKey(playerId);
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
        var myChallengeKey = _database.GetMyChallengeKey(model.CreatorId);
        if (myChallengeKey != null)
        {
            ModelState.AddModelError(nameof(model.CreatorId), "You already have a challenge!");
        }
        if (!ModelState.IsValid)
        {
            return View(model);
        }
        
        // Create a new challenge.
        var player = _database.GetOrAddPlayer(model.CreatorId);
        var challenge = new Challenge(player)
        {
            RoleRule = model.RoleRule,
            Message = model.Message,
            Permission = model.Permission,
            TimeLimit = model.TimeLimit,
        };
        var challengeId = _counter.GetUniqueNo();
        _database.CreateChallenge(challengeId, challenge);
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
        var challenge = _database.GetChallenge(id);
        if (challenge == null)
        {
            // Challenge not found.
            return NotFound();
        }
        var model = new ChallengeViewModel
        {
            RoomId = id,
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
        var challenge = _database.GetChallenge(model.Id);
        if (challenge != null && challenge.Creator.Id == model.PlayerId)
        {
            _database.DeleteChallenge(model.Id);
        }
        return RedirectToAction(nameof(Index));
    }

    public async Task ListenChallenge(int id)
    {
        var pusher = await HttpContext.AcceptWebSocketClient();
        var challenge = _database.GetChallenge(id);
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