using Aiursoft.ChessServer.Data;
using Aiursoft.ChessServer.Models;
using Aiursoft.ChessServer.Models.ViewModels;
using Aiursoft.ChessServer.Services;
using Aiursoft.UiStack.Navigation;
using Microsoft.AspNetCore.Mvc;

namespace Aiursoft.ChessServer.Controllers;

public class LobbyController(InMemoryDatabase database, LlmModelCatalog catalog, Microsoft.Extensions.Options.IOptions<Configuration.OpenAIConfiguration> options) : ChessController
{
    [RenderInNavBar(NavGroupName = "Features", NavGroupOrder = 1, CascadedLinksGroupName = "Chess",
        CascadedLinksIcon = "crown", CascadedLinksOrder = 1, LinkText = "Play chess", LinkOrder = 1)]
    public async Task<IActionResult> Index()
    {
        string[] models;
        try { models = await catalog.GetModelsAsync(HttpContext.RequestAborted); }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException or KeyNotFoundException or InvalidOperationException)
        { models = []; }
        return this.StackView(new LobbyViewModel
        {
            LlmModels = models,
            PreferredLlmModel = options.Value.Model,
            MyGames = database.GetRooms().Where(p => p.Value is AcceptedChallenge game && !game.Game.Board.IsEndGame && game.GetPlayerColor(PlayerId) != "m").ToArray(),
            OpenChallenges = database.GetPublicOpenChallenges(),
            OngoingChallenges = database.GetOnGoingOpenChallenges()
        });
    }
}
