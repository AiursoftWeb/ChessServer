using Aiursoft.ChessServer.Data;
using Aiursoft.ChessServer.Models;
using Aiursoft.CSTools.Services;
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
        if (!ModelState.IsValid)
        {
            return View(model);
        }
        
        var player = _database.GetOrAddPlayer(model.CreatorId);
        var challenge = new Challenge(player)
        {
            RoleRule = model.RoleRule,
            Message = model.Message,
            Permission = model.Permission,
            TimeLimit = model.TimeLimit,
        };
        var roomId = _counter.GetUniqueNo();
        _database.CreateChallenge(roomId, challenge);
        return RedirectToAction(nameof(Challenge), new { id = roomId });
    }
    
    [HttpGet]
    public IActionResult Challenge(int id)
    {
        var challenge = _database.GetChallenge(id);
        if (challenge == null)
        {
            return RedirectToAction(nameof(Index));
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
}