using Aiursoft.Scanner.Abstractions;
using Chess;
using System.Collections.Concurrent;

namespace Aiursoft.ChessServer.Data;

public class Game
{
    public bool Ended { get; set; }
    public int Key { get; set; }
}

public class InMemoryDatabase : ISingletonDependency
{
    public ConcurrentDictionary<int, ChessBoard> Boards { get; private set; } = new ConcurrentDictionary<int, ChessBoard>();

    public Game[] GetActiveGames()
    { 
        return Boards.Select(t => new Game 
        {
            Ended = t.Value.IsEndGame,
            Key = t.Key
        }).ToArray();
    }

    public ChessBoard GetOrAdd(int id)
    {
        if (!Boards.ContainsKey(id))
        {
            Boards.TryAdd(id, new ChessBoard());
        }
        return Boards[id];
    }
}
