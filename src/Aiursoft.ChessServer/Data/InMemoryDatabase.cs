using Aiursoft.Scanner.Abstractions;
using System.Collections.Concurrent;
using Aiursoft.ChessServer.Models;

namespace Aiursoft.ChessServer.Data;

public class InMemoryDatabase : ISingletonDependency
{
    private ConcurrentDictionary<int, Game> Games { get; } = new();

    public Game[] GetActiveGames()
    { 
        return Games.Values.ToArray();
    }

    public Game GetOrAddGame(int id)
    {
        return Games.GetOrAdd(id, _ => new Game());
    }
}
