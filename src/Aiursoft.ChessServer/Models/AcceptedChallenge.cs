using Aiursoft.AiurObserver;

namespace Aiursoft.ChessServer.Models;

public class AcceptedChallenge : Challenge
{
    private readonly bool _creatorIsWhite;
    private string? _completedPgn;
    public Guid HistoryId { get; } = Guid.NewGuid();
    public SemaphoreSlim ArchiveLock { get; } = new(1, 1);
    public bool Archived { get; set; }
    public TimeSpan WhiteRemaining { get; private set; }
    public TimeSpan BlackRemaining { get; private set; }
    public DateTimeOffset LastClockUpdate { get; private set; }
    public DateTimeOffset? FinishedAt { get; private set; }
    public bool IsTimed => TimeLimit != TimeSpan.MaxValue;

    // All clock and board changes share the move lock, including the final timeout decision.
    public bool UpdateClock(DateTimeOffset now)
    {
        lock (Game.MovePieceLock)
        {
            var wasEnded = Game.Board.IsEndGame;
            if (!wasEnded && IsTimed && now > LastClockUpdate)
            {
                var elapsed = now - LastClockUpdate;
                if (Game.Board.Turn == Chess.PieceColor.White)
                    WhiteRemaining = elapsed >= WhiteRemaining ? TimeSpan.Zero : WhiteRemaining - elapsed;
                else
                    BlackRemaining = elapsed >= BlackRemaining ? TimeSpan.Zero : BlackRemaining - elapsed;
                if (WhiteRemaining == TimeSpan.Zero || BlackRemaining == TimeSpan.Zero)
                    Game.Board.EndByTimeout(Game.Board.Turn);
            }
            if (now > LastClockUpdate) LastClockUpdate = now;
            if (Game.Board.IsEndGame) FinishedAt ??= now;
            return !wasEnded && Game.Board.IsEndGame;
        }
    }

    public string Result => Game.Board.EndGame?.WonSide == Chess.PieceColor.White ? "1-0"
        : Game.Board.EndGame?.WonSide == Chess.PieceColor.Black ? "0-1" : Game.Board.IsEndGame ? "1/2-1/2" : "*";

    public string Pgn()
    {
        lock (Game.MovePieceLock)
        {
            if (_completedPgn != null) return _completedPgn;
            foreach (var key in new[] { "White", "Black", "Date", "Result", "TimeControl" }) Game.Board.RemoveHeader(key);
            Game.Board.AddHeader("White", GetWhitePlayer().NickName.Replace('"', '\'').Replace('\n', ' ').Replace('\r', ' '));
            Game.Board.AddHeader("Black", GetBlackPlayer().NickName.Replace('"', '\'').Replace('\n', ' ').Replace('\r', ' '));
            Game.Board.AddHeader("Date", GameStartTime.ToString("yyyy.MM.dd", System.Globalization.CultureInfo.InvariantCulture));
            Game.Board.AddHeader("Result", Result);
            Game.Board.AddHeader("TimeControl", IsTimed ? ((int)TimeLimit.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture) : "-");
            var pgn = Game.Board.ToPgn();
            if (Game.Board.IsEndGame) _completedPgn = pgn;
            return pgn;
        }
    }

    public AcceptedChallenge(
        Player creator, 
        Player accepter, 
        string message, 
        RoleRule roleRule, 
        TimeSpan timeLimit, 
        ChallengePermission permission,
        AsyncObservable<string> challengeChangedChannel)
        : base(creator, message, roleRule, timeLimit, permission)
    {
        Accepter = accepter;
        Game = new Game();
        WhiteRemaining = BlackRemaining = timeLimit;
        LastClockUpdate = DateTimeOffset.UtcNow;
        ChallengeChangedChannel = challengeChangedChannel;
        _creatorIsWhite = RoleRule switch
        {
            RoleRule.Random => new Random().Next(0, 2) == 0,
            RoleRule.CreatorWhite => true,
            RoleRule.AccepterWhite => false,
            _ => _creatorIsWhite
        };
    }

    public Player Accepter { get; set; }
    public Game Game { get; set; }
    
    public AsyncObservable<ChatMessage> ChatChannel { get; init; } = new();
    
    public Player GetWhitePlayer() => _creatorIsWhite ? Creator : Accepter;
    public Player GetBlackPlayer() => _creatorIsWhite ? Accepter : Creator;
    
    public DateTime GameStartTime { get; set; } = DateTime.UtcNow;

    public Player GetTurnPlayer()
    {
        return Game.Board.Turn.AsChar.ToString().Equals("w", StringComparison.CurrentCultureIgnoreCase) 
            ? GetWhitePlayer() : GetBlackPlayer(); 
    }

    public string GetPlayerColor(Guid playerId)
    {
        // returns : w,b, or m (monitor)
        if (GetWhitePlayer().Id == playerId)
        {
            return "w";
        }
        if (GetBlackPlayer().Id == playerId)
        {
            return "b";
        }
        return "m";
    }
}