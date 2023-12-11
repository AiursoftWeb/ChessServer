using Aiursoft.AiurObserver;
using Chess;

namespace Aiursoft.ChessServer.Models;

public class Game
{
    public ChessBoard Board { get; } = new();

    public AsyncObservable<string> BoardChannel { get; } = new();
    
    public object MovePieceLock { get; } = new();
}