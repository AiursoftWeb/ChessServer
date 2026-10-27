using System.ComponentModel.DataAnnotations;
using Aiursoft.ChessServer.Models;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.ChessServer.Models.ViewModels;

public class LobbyViewModel : UiStackLayoutViewModel
{
    public LobbyViewModel() {
        PageTitle = "Chess";
    }
    public IReadOnlyCollection<KeyValuePair<int, Challenge>> OpenChallenges { get; init;
    } = [];
    public IReadOnlyCollection<KeyValuePair<int, Challenge>> OngoingChallenges { get; init;
    } = [];
}
