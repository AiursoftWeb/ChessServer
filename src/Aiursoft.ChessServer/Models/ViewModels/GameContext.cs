using Chess;

namespace Aiursoft.ChessServer.Models.ViewModels;

public class GameContext(Game game, int id)
{
    public int Id { get; } = id;
    public Dictionary<string, string> Links { get; } = new()
    {
        { "ascii", $"games/{id}.ascii" },
        { "fen", $"games/{id}.fen" },
        { "pgn", $"games/{id}.pgn" },
        { "html", $"games/{id}.html" },
        { "websocket", $"games/{id}.ws" },
        { "move-post", $"games/{id}/move/{{player}}/{{move_algebraic_notation}}" }
    };

    public char Turn { get; } = game.Board.Turn.AsChar;
    public bool Ended { get; } = game.Board.IsEndGame;
    public EndgameType? End { get; } = game.Board.EndGame?.EndgameType;
    public PieceColor? Won { get; } = game.Board.EndGame?.WonSide;
    public int MoveIndex { get; } = game.Board.MoveIndex;
    public bool WhiteKingChecked { get; } = game.Board.WhiteKingChecked;
    public bool BlackKingChecked { get; } = game.Board.BlackKingChecked;
    public int Listeners { get; } = game.FenChangedChannel.GetListenerCount();
}