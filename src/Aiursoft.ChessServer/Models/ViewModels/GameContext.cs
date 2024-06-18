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
        { "websocket", $"games/{id}.ws" }
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

public class ChallengeContext(AcceptedChallenge challenge, int id)
{
    public GameContext Game { get; } = new(challenge.Game, id);

    public string WhitePlayer { get; } = challenge.GetWhitePlayer().NickName;

    public string BlackPlayer { get; } = challenge.GetBlackPlayer().NickName;
    
    public string Creator { get; } = challenge.Creator.NickName;
    
    public string Accepter { get; } = challenge.Accepter.NickName;
    
    public string Message { get; } = challenge.Message;
    
    public RoleRule RoleRule { get; } = challenge.RoleRule;
    
    public TimeSpan TimeLimit { get; } = challenge.TimeLimit;
    
    public ChallengePermission Permission { get; } = challenge.Permission;

    public DateTime StartTime { get; } = challenge.GameStartTime;
}
