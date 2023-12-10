using Aiursoft.Scanner.Abstractions;
using System.Collections.Concurrent;
using Aiursoft.ChessServer.Models;

namespace Aiursoft.ChessServer.Data;

public class InMemoryDatabase : ISingletonDependency
{
    private ConcurrentDictionary<int, Game> Games { get; } = new();

    public GameContext[] GetActiveGames()
    {
        return Games.Select(g => new GameContext(g.Value, g.Key)).ToArray();
    }

    public Game GetOrAddGame(int id)
    {
        lock (this)
        {
            return Games.GetOrAdd(id, _ => new Game());
        }
    }
}