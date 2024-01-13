using System.Collections.Concurrent;

namespace Aiursoft.ChessServer.Models;

public class IndexViewModel
{
    public ConcurrentDictionary<int, Challenge> Challenges { get; }

    public IndexViewModel(ConcurrentDictionary<int, Challenge> challenges)
    {
        Challenges = challenges;
    }
}