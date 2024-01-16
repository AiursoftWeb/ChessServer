using Chess;

namespace Aiursoft.ChessServer.Models.ViewModels;

public class GameContext
{
    public GameContext(Game game, int id)
    {
        Id = id;
        Turn = game.Board.Turn.AsChar;
        Ended = game.Board.IsEndGame;
        End = game.Board.EndGame?.EndgameType;
        Won = game.Board.EndGame?.WonSide;
        MoveIndex = game.Board.MoveIndex;
        WhiteKingChecked = game.Board.WhiteKingChecked;
        BlackKingChecked = game.Board.BlackKingChecked;
        Links = new Dictionary<string, string>
        {
            { "ascii", $"games/{id}.ascii" },
            { "fen", $"games/{id}.fen" },
            { "pgn", $"games/{id}.pgn" },
            { "html", $"games/{id}.html" },
            { "websocket", $"games/{id}.ws" },
            { "move-post", $"games/{id}/move/{{player}}/{{move_algebraic_notation}}" }
        };
        Listeners = game.FenChangedChannel.GetListenerCount();
    }


    public int Id { get; }
    public Dictionary<string, string> Links { get; }
    public char Turn { get; }
    public bool Ended { get; }
    public EndgameType? End { get; }
    public PieceColor? Won { get; }
    public int MoveIndex { get; }
    public bool WhiteKingChecked { get; }
    public bool BlackKingChecked { get; }
    public int Listeners { get; }
}