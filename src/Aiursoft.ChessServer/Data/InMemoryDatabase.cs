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
    public ConcurrentDictionary<int, ChessBoard> Boards { get; } = new();
    public ConcurrentDictionary<int, Channel> Channels { get; } = new();

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
        return Boards.GetOrAdd(id, _ => new ChessBoard());
    }
    
    public (Channel, SemaphoreSlim) ListenChannel(int id)
    {
        var channel = Channels.GetOrAdd(id, _ => new Channel());
        var blocker = new SemaphoreSlim(0);
        channel.HasNewMessageBlocker.Add(blocker);
        return (channel, blocker);
    }
}
