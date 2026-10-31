using Aiursoft.AiurObserver;
using Chess;

namespace Aiursoft.ChessServer.Models;

public class Game
{
    public bool IsThinking { get; set; }
    public string? ComputerError { get; set; }
    public CancellationTokenSource? SearchCancellation { get; set; }

    public static ChessBoard NewBoard() => new() { AutoEndgameRules = AutoEndgameRules.All & ~AutoEndgameRules.InsufficientMaterial };
    public ChessBoard Board { get; set; } = NewBoard();

    public AsyncObservable<string> FenChangedChannel { get; } = new();
    
    public object MovePieceLock { get; } = new();
}