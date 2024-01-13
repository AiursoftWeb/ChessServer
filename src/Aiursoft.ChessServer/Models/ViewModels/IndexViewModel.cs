namespace Aiursoft.ChessServer.Models;

public class IndexViewModel
{
    public required IReadOnlyCollection<KeyValuePair<int, Challenge>> Challenges { get; init; }
}