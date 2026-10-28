using Aiursoft.AiurObserver;

namespace Aiursoft.ChessServer.Models;

public class AcceptedChallenge : Challenge
{
    private bool _creatorIsWhite;
    private string? _completedPgn;
    public Guid HistoryId { get; set; } = Guid.NewGuid();
    public SemaphoreSlim ArchiveLock { get; } = new(1, 1);
    public bool Archived { get; set; }
    public TimeSpan WhiteRemaining { get; set; }
    public TimeSpan BlackRemaining { get; set; }
    public DateTimeOffset LastClockUpdate { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public DateTimeOffset AbortAt { get; set; } = DateTimeOffset.UtcNow.AddMinutes(1);
    public DateTimeOffset LastCheckpoint { get; set; } = DateTimeOffset.UtcNow;
    public long Revision { get; set; }
    public Guid? DrawOfferedBy { get; set; }
    public string? OutcomeReason { get; set; }
    public DateTimeOffset WhiteSeenAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset BlackSeenAt { get; set; } = DateTimeOffset.UtcNow;
    public List<CommandReceipt> Receipts { get; set; } = [];
    public bool CreatorIsWhite { get => _creatorIsWhite; set => _creatorIsWhite = value; }
    public void ClearPgnCache() => _completedPgn = null;
    public bool IsTimed => TimeLimit != TimeSpan.MaxValue;

    // All clock and board changes share the move lock, including the final timeout decision.
    public bool UpdateClock(DateTimeOffset now)
    {
        lock (Game.MovePieceLock)
        {
            var wasEnded = Game.Board.IsEndGame;
            if (!wasEnded && !MatingMaterial.CanMate(Game.Board.ToFen(), 'w') && !MatingMaterial.CanMate(Game.Board.ToFen(), 'b'))
            {
                Game.Board.Draw(); OutcomeReason = "InsufficientMaterial"; Revision++;
            }
            if (!Game.Board.IsEndGame && IsTimed && now > LastClockUpdate)
            {
                var elapsed = now - LastClockUpdate;
                if (Game.Board.Turn == Chess.PieceColor.White)
                    WhiteRemaining = elapsed >= WhiteRemaining ? TimeSpan.Zero : WhiteRemaining - elapsed;
                else
                    BlackRemaining = elapsed >= BlackRemaining ? TimeSpan.Zero : BlackRemaining - elapsed;
                if (WhiteRemaining == TimeSpan.Zero || BlackRemaining == TimeSpan.Zero)
                {
                    if (MatingMaterial.CanMate(Game.Board.ToFen(), Game.Board.Turn == Chess.PieceColor.White ? 'b' : 'w'))
                        Game.Board.EndByTimeout(Game.Board.Turn);
                    else Game.Board.Draw();
                    OutcomeReason = Game.Board.EndGame!.WonSide == null ? "TimeoutDraw" : "Timeout";
                    Revision++;
                }
            }
            if (now > LastClockUpdate) LastClockUpdate = now;
            if (Game.Board.IsEndGame) FinishedAt ??= now;
            if (!wasEnded && Game.Board.IsEndGame) LastActivity = now;
            return !wasEnded && Game.Board.IsEndGame;
        }
    }

    public string Result => OutcomeReason is "Aborted" or "Abandoned" ? "*" : Game.Board.EndGame?.WonSide == Chess.PieceColor.White ? "1-0"
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
            if (OutcomeReason is "Aborted" or "Abandoned" && pgn.EndsWith("1/2-1/2", StringComparison.Ordinal)) pgn = pgn[..^7] + "*";
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