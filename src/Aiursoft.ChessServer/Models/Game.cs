using AiurObserver;
using Chess;

namespace Aiursoft.ChessServer.Models;

public class Game
{
    public ChessBoard Board { get; set; } = new();

    public AsyncObservable<string> Channel { get; set; } = new();
    
    public object MovePieceLock { get; } = new();
}