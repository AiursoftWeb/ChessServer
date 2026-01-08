using Aiursoft.ChessServer.Data;
using Aiursoft.ChessServer.Models.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace Aiursoft.ChessServer.Controllers;

public class HomeController(InMemoryDatabase database) : Controller
{
    public IActionResult Index()
    {
        var model = new IndexViewModel
        {
            Challenges = database.GetPublicOpenChallenges()
        };
        return View(model);
    }
    
    [HttpGet]
    public IActionResult Watch()
    {
        var model = new IndexViewModel
        {
            Challenges = database.GetOnGoingOpenChallenges()
        };
        return View(nameof(Index), model);
    }
}