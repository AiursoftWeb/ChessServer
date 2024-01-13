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

    public IActionResult Index()
    {
        var model = new IndexViewModel(_database.Challenges);
        return View(model);
    }
    
    public IActionResult Create(Guid playerId)
    {
        var iHaveAChallenge = _database.Challenges.FirstOrDefault(t => t.Value.Creator.Id == playerId);
        if (iHaveAChallenge.Value != null)
        {
            return RedirectToAction(nameof(Room), new { id = iHaveAChallenge.Key });
        }
        var model = new CreateChallengeViewModel();
        return View(model);
    }

    [HttpPost]
    public IActionResult Create(CreateChallengeViewModel model)
    {
        var player = _database.GetOrAddPlayer(model.CreatorId);
        var challenge = new Challenge(player)
        {
            RoleRule = model.RoleRule,
            Message = model.Message,
            Permission = model.Permission,
            TimeLimit = model.TimeLimit,
        };
        var uniqueId = _counter.GetUniqueNo();
        _database.Challenges.TryAdd(uniqueId, challenge);
        return RedirectToAction(nameof(Room), new { id = uniqueId });
    }

    public IActionResult Room(int id)
    {
        // Not implemented.
        return Ok();
    }
}