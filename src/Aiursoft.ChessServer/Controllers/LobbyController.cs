using Aiursoft.ChessServer.Data;
using Aiursoft.ChessServer.Models.ViewModels;
using Aiursoft.ChessServer.Services;
using Aiursoft.UiStack.Navigation;
using Microsoft.AspNetCore.Mvc;

namespace Aiursoft.ChessServer.Controllers;

public class LobbyController(InMemoryDatabase database) : ChessController
{
    [RenderInNavBar(NavGroupName = "Features", NavGroupOrder = 1, CascadedLinksGroupName = "Chess",
        CascadedLinksIcon = "crown", CascadedLinksOrder = 1, LinkText = "Play chess", LinkOrder = 1)]
    public IActionResult Index() => this.StackView(new LobbyViewModel
    {
        OpenChallenges = database.GetPublicOpenChallenges(),
        OngoingChallenges = database.GetOnGoingOpenChallenges()
    });
}
