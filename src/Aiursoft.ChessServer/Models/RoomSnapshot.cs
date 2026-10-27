using Chess;

namespace Aiursoft.ChessServer.Models;

public class RoomSnapshot
{
    public required Guid CreatorId { get; set; }
    public required string CreatorName { get; set; }
    public Guid? AccepterId { get; set; }
    public string? AccepterName { get; set; }
    public required string Message { get; set; }
    public RoleRule Rule { get; set; }
    public long TimeTicks { get; set; }
    public ChallengePermission Permission { get; set; }
    public int? Difficulty { get; set; }
    public DateTimeOffset Activity { get; set; }
    public Guid HistoryId { get; set; }
    public bool CreatorWhite { get; set; }
    public long WhiteTicks { get; set; }
    public long BlackTicks { get; set; }
    public DateTimeOffset ClockAt { get; set; }
    public DateTimeOffset AbortAt { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public string[] Moves { get; set; } = [];
    public string? EndReason { get; set; }
    public string? Winner { get; set; }
    public string? OutcomeReason { get; set; }
    public Guid? DrawOfferedBy { get; set; }
    public long Revision { get; set; }
    public List<CommandReceipt> Receipts { get; set; } = [];

    public static RoomSnapshot Capture(Challenge room)
    {
        var snapshot = new RoomSnapshot { CreatorId = room.Creator.Id, CreatorName = room.Creator.NickName,
            Message = room.Message, Rule = room.RoleRule, TimeTicks = room.TimeLimit.Ticks, Permission = room.Permission,
            Difficulty = room.Difficulty, Activity = room.LastActivity };
        if (room is not AcceptedChallenge accepted) return snapshot;
        lock (accepted.Game.MovePieceLock)
        {
            snapshot.AccepterId = accepted.Accepter.Id; snapshot.AccepterName = accepted.Accepter.NickName;
            snapshot.HistoryId = accepted.HistoryId; snapshot.CreatorWhite = accepted.CreatorIsWhite;
            snapshot.WhiteTicks = accepted.WhiteRemaining.Ticks; snapshot.BlackTicks = accepted.BlackRemaining.Ticks;
            snapshot.AbortAt = accepted.AbortAt; snapshot.ClockAt = accepted.LastClockUpdate; snapshot.StartedAt = accepted.GameStartTime;
            snapshot.FinishedAt = accepted.FinishedAt; snapshot.Moves = accepted.Game.Board.MovesToSan.ToArray();
            snapshot.EndReason = accepted.Game.Board.EndGame?.EndgameType.ToString();
            snapshot.Winner = accepted.Game.Board.EndGame?.WonSide?.AsChar.ToString();
            snapshot.OutcomeReason = accepted.OutcomeReason; snapshot.DrawOfferedBy = accepted.DrawOfferedBy;
            snapshot.Revision = accepted.Revision; snapshot.Receipts = accepted.Receipts.ToList();
        }
        return snapshot;
    }

    public Challenge Restore()
    {
        var creator = new Player(CreatorId) { NickName = CreatorName };
        var room = new Challenge(creator, Message, Rule, TimeSpan.FromTicks(TimeTicks), Permission)
            { Difficulty = Difficulty, LastActivity = Activity };
        if (AccepterId == null) return room;
        var accepted = new AcceptedChallenge(creator, new Player(AccepterId.Value) { NickName = AccepterName! }, Message,
            Rule, room.TimeLimit, Permission, room.ChallengeChangedChannel) { Difficulty = Difficulty };
        Apply(accepted);
        return accepted;
    }

    public void Apply(AcceptedChallenge room)
    {
        lock (room.Game.MovePieceLock)
        {
            var board = Game.NewBoard();
            foreach (var move in Moves) board.Move(move);
            if (EndReason != null && !board.IsEndGame)
            {
                if (Winner == null) board.Draw();
                else if (EndReason == "Timeout") board.EndByTimeout(Winner == "w" ? PieceColor.Black : PieceColor.White);
                else board.Resign(Winner == "w" ? PieceColor.Black : PieceColor.White);
            }
            room.Game.Board = board; room.ClearPgnCache(); room.HistoryId = HistoryId; room.CreatorIsWhite = CreatorWhite;
            room.WhiteRemaining = TimeSpan.FromTicks(WhiteTicks); room.BlackRemaining = TimeSpan.FromTicks(BlackTicks);
            room.AbortAt = AbortAt == default ? new DateTimeOffset(StartedAt).AddMinutes(1) : AbortAt;
            room.LastClockUpdate = ClockAt; room.GameStartTime = StartedAt; room.FinishedAt = FinishedAt;
            room.LastActivity = Activity; room.OutcomeReason = OutcomeReason; room.DrawOfferedBy = DrawOfferedBy;
            room.Revision = Revision; room.Receipts = Receipts.ToList();
        }
    }
}
