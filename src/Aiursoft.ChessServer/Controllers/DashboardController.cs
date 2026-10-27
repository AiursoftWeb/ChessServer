using Aiursoft.ChessServer.Data;
using Aiursoft.ChessServer.Models.ViewModels;
using Aiursoft.ChessServer.Services;
using Microsoft.AspNetCore.Mvc;

namespace Aiursoft.ChessServer.Controllers;

public class DashboardController(InMemoryDatabase database) : ChessController
{
    public IActionResult Index() => this.StackView(new LobbyViewModel
    {
        OpenChallenges = database.GetPublicOpenChallenges(),
        OngoingChallenges = database.GetOnGoingOpenChallenges()
    }, "~/Views/Lobby/Index.cshtml");
}
