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
        var model = new IndexViewModel(_database.Challenges);
        return View(model);
    }

    [HttpGet]
    public IActionResult Auto(Guid playerId)
    {
        // I created a challenge. Go to my challenge.
        var iHaveAChallenge = _database.Challenges.FirstOrDefault(t => t.Value.Creator.Id == playerId);
        if (iHaveAChallenge.Value != null)
        {
            return RedirectToAction(nameof(Challenge), new { id = iHaveAChallenge.Key, playerId });
        }
        
        // Exists a public challenge. Go to that challenge.
        var otherChallenge = _database.Challenges.FirstOrDefault(t => t.Value.Permission == ChallengePermission.Public);
        if (otherChallenge.Value != null)
        {
            return RedirectToAction(nameof(Challenge), new { id = otherChallenge.Key, playerId });
        }
        
        // Create a new challenge.
        return RedirectToAction(nameof(Create), new { playerId });
    }
    
    [HttpGet]
    public IActionResult Create(Guid playerId)
    {
        var iHaveAChallenge = _database.Challenges.FirstOrDefault(t => t.Value.Creator.Id == playerId);
        if (iHaveAChallenge.Value != null)
        {
            return RedirectToAction(nameof(Challenge), new { id = iHaveAChallenge.Key, playerId });
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
        _database.Challenges.TryAdd(roomId, challenge);
        return RedirectToAction(nameof(Challenge), new { id = roomId, playerId = model.CreatorId });
    }

    [HttpGet]
    public IActionResult Challenge(int id, Guid playerId)
    {
        var player = _database.GetOrAddPlayer(playerId);
        var challenge = _database.GetOrAddChallenge(id, player);
        var model = new ChallengeViewModel()
        {
            RoomId = id,
            PlayerId = playerId,
            IsCreator = challenge.Creator.Id == playerId,
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
        var player = _database.GetOrAddPlayer(model.PlayerId);
        var challenge = _database.GetOrAddChallenge(model.Id, player);
        if (challenge.Creator.Id == model.PlayerId)
        {
            _database.Challenges.TryRemove(model.Id, out _);
        }
        return RedirectToAction(nameof(Index));
    }
}