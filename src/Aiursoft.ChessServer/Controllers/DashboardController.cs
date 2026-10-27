using Microsoft.AspNetCore.Mvc;

namespace Aiursoft.ChessServer.Controllers;

public class DashboardController : ChessController
{
    public IActionResult Index() => RedirectToAction("Index", "Lobby");
}
