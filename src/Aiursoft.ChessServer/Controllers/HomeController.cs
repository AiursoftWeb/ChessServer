using Microsoft.AspNetCore.Mvc;

namespace Aiursoft.ChessServer.Controllers;

public class HomeController : ControllerBase
{
    public IActionResult Index()
    {
        return this.Ok("Welcome to chess server! Please go to '/games/12345.json' to view more.");
    }
}
