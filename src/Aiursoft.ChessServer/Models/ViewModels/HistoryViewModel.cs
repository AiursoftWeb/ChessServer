using Aiursoft.ChessServer.Entities;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.ChessServer.Models.ViewModels;

public class HistoryViewModel : UiStackLayoutViewModel
{
    public HistoryViewModel() { PageTitle = "My games"; }
    public required DateOnly Date { get; init; }
    public int OffsetMinutes { get; init; }
    public int Page { get; init; }
    public bool HasMore { get; init; }
    public required List<CompletedGame> Games { get; init; }
}

public class ReplayViewModel : UiStackLayoutViewModel
{
    public ReplayViewModel() { PageTitle = "Replay game"; }
    public required CompletedGame Game { get; init; }
    public bool IsBlack { get; init; }
}
