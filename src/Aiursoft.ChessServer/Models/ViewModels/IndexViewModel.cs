namespace Aiursoft.ChessServer.Models.ViewModels;

public class IndexViewModel
{
    public required IReadOnlyCollection<KeyValuePair<int, Challenge>> Challenges { get; init; }
}