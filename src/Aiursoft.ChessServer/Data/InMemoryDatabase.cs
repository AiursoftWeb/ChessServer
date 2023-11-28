using Aiursoft.Scanner.Abstractions;
using Chess;
using System.Collections.Concurrent;
using Aiursoft.ChessServer.Models;

namespace Aiursoft.ChessServer.Data;

public class Game
{
    public bool Ended { get; set; }
    public int Key { get; set; }
}

public class InMemoryDatabase : ISingletonDependency
{
    private ConcurrentDictionary<int, ChessBoard> Boards { get; } = new();
    private ConcurrentDictionary<int, Channel> Channels { get; } = new();

    public Game[] GetActiveGames()
    { 
        return Boards.Select(t => new Game 
        {
            Ended = t.Value.IsEndGame,
            Key = t.Key
        }).ToArray();
    }

    public ChessBoard GetOrAddBoard(int id)
    {
        return Boards.GetOrAdd(id, _ => new ChessBoard());
    }
    
    public Channel GetOrAddChannel(int id)
    {
        return Channels.GetOrAdd(id, _ => new Channel());
    }
}
