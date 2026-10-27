using Aiursoft.AiurObserver;
using Chess;

namespace Aiursoft.ChessServer.Models;

public class Game
{
    public bool IsThinking { get; set; }

    public ChessBoard Board { get; } = new() { AutoEndgameRules = AutoEndgameRules.All };

    public AsyncObservable<string> FenChangedChannel { get; } = new();
    
    public object MovePieceLock { get; } = new();
}