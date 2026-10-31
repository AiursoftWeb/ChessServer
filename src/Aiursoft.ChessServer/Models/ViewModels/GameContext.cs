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
    
    public bool WhiteGuest { get; } = challenge.GetWhitePlayer().IsGuest;
    public bool BlackGuest { get; } = challenge.GetBlackPlayer().IsGuest;

    public string Creator { get; } = challenge.Creator.NickName;
    
    public string Accepter { get; } = challenge.Accepter.NickName;
    
    public string Message { get; } = challenge.Message;
    
    public RoleRule RoleRule { get; } = challenge.RoleRule;
    
    public TimeSpan TimeLimit { get; } = challenge.TimeLimit;
    
    public ChallengePermission Permission { get; } = challenge.Permission;

    public DateTime StartTime { get; } = challenge.GameStartTime;
    public Guid HistoryId { get; } = challenge.HistoryId;
    public bool Timed { get; } = challenge.IsTimed;
    public double? WhiteMilliseconds { get; } = challenge.IsTimed ? challenge.WhiteRemaining.TotalMilliseconds : null;
    public double? BlackMilliseconds { get; } = challenge.IsTimed ? challenge.BlackRemaining.TotalMilliseconds : null;
    public string Result { get; } = challenge.Result;
    public string? EndReason { get; } = challenge.OutcomeReason ?? challenge.Game.Board.EndGame?.EndgameType.ToString();
    public string Position { get; } = challenge.Game.Board.ToFen();
    public string[] Moves { get; } = challenge.Game.Board.MovesToSan.ToArray();
    public long Revision { get; } = challenge.Revision;
    public long SampledAt { get; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    public bool Computer { get; } = challenge.IsComputer;
    public int IncrementSeconds { get; } = challenge.IncrementSeconds;
    public int? InitialMinutes { get; } = challenge.IsTimed ? (int)challenge.TimeLimit.TotalMinutes : null;
    public bool CanUndo { get; } = challenge.IsComputer && !challenge.Game.Board.IsEndGame && challenge.UndoPly >= 0;
    public Guid? RematchOfferedBy { get; } = challenge.RematchOfferedBy;
    public int? NextGameId { get; } = challenge.NextGameId;
    public bool Thinking { get; } = challenge.Game.IsThinking;
    public string? ComputerError { get; } = challenge.Game.ComputerError;
    public string WhiteId { get; } = challenge.GetWhitePlayer().Id.ToString();
    public string BlackId { get; } = challenge.GetBlackPlayer().Id.ToString();
    public string? DrawOfferedBy { get; } = challenge.DrawOfferedBy?.ToString();
    public double WhiteAbsentSeconds { get; } = challenge.IsComputer && !challenge.CreatorIsWhite ? 0 : Math.Max(0, (DateTimeOffset.UtcNow - challenge.WhiteSeenAt).TotalSeconds);
    public double BlackAbsentSeconds { get; } = challenge.IsComputer && challenge.CreatorIsWhite ? 0 : Math.Max(0, (DateTimeOffset.UtcNow - challenge.BlackSeenAt).TotalSeconds);
}
