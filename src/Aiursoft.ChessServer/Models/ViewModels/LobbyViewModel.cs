using Aiursoft.UiStack.Layout;

namespace Aiursoft.ChessServer.Models.ViewModels;

public class LobbyViewModel : UiStackLayoutViewModel
{
    public LobbyViewModel()
    {
        PageTitle = "Chess";
    }
    public Dictionary<int, string> RoomStates { get; init; } = [];
    public IReadOnlyCollection<KeyValuePair<int, Challenge>> Rooms { get; init; } = [];
    public IReadOnlyCollection<KeyValuePair<int, Challenge>> OpenChallenges { get; init; } = [];
    public IReadOnlyCollection<KeyValuePair<int, Challenge>> OngoingChallenges { get; init; } = [];
    public IReadOnlyCollection<KeyValuePair<int, Challenge>> MyGames { get; init; } = [];
}
