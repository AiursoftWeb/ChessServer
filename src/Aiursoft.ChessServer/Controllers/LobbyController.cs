using Aiursoft.ChessServer.Data;
using Aiursoft.ChessServer.Models;
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
        RoomStates = database.GetRooms().Where(p => p.Value is ManagedRoom).ToDictionary(p => p.Key, p => ((ManagedRoom)p.Value).CurrentGameId is not { } gameId ? "waiting" : database.GetAcceptedChallenge(gameId)?.Game.Board.IsEndGame == true ? "finished" : "playing"),
        Rooms = database.GetRooms().Where(p => p.Value is ManagedRoom room && (room.Permission == ChallengePermission.Public || room.Members.Any(m => m.Player.Id == PlayerId))).ToArray(),
        MyGames = database.GetRooms().Where(p => p.Value is AcceptedChallenge { ParentRoomId: null } game && !game.Game.Board.IsEndGame && game.GetPlayerColor(PlayerId) != "m").ToArray(),
        OpenChallenges = database.GetPublicOpenChallenges(),
        OngoingChallenges = database.GetOnGoingOpenChallenges()
    });
}
