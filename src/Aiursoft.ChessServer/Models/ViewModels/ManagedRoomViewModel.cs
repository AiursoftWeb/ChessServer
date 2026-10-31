using Aiursoft.UiStack.Layout;

namespace Aiursoft.ChessServer.Models.ViewModels;

public class ManagedRoomViewModel : UiStackLayoutViewModel
{
    public ManagedRoomViewModel() { PageTitle = "Chess room"; }
    public int Id { get; init; }
    public string Message { get; init; } = "";
    public string[] Models { get; init; } = [];
    public RoomViewModel? Game { get; init; }
}
