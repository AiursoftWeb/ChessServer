using Aiursoft.AiurObserver;
using Chess;

namespace Aiursoft.ChessServer.Models;

public class Game
{
    public bool IsThinking { get; set; }
    public ComputerProgress? ComputerProgress { get; set; }
    public string? ComputerError { get; set; }
    public CancellationTokenSource? SearchCancellation { get; set; }

    public static ChessBoard NewBoard() => new() { AutoEndgameRules = AutoEndgameRules.All & ~AutoEndgameRules.InsufficientMaterial };
    public ChessBoard Board { get; set; } = NewBoard();

    public AsyncObservable<string> FenChangedChannel { get; } = new();
    
    public object MovePieceLock { get; } = new();
}

public record ComputerProgress
{
    public int Attempt { get; init; }
    public int MaxAttempts { get; init; }
    public int TimeoutSeconds { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public bool Queued { get; init; }
}
