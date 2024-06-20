using Aiursoft.AiurObserver;

namespace Aiursoft.ChessServer.Models;

public class Challenge(
    Player creator, 
    string message, 
    RoleRule roleRule, 
    TimeSpan timeLimit, 
    ChallengePermission permission)
{
    public Player Creator { get; set; } = creator;
    public string Message { get; set; } = message;
    
    public RoleRule RoleRule { get; set; } = roleRule;
    
    public TimeSpan TimeLimit { get; set; } = timeLimit;
    
    public ChallengePermission Permission { get; set; } = permission;
    
    // Possible messages:
    // Player joined: p-joined-{player-nick-name}
    // Player left: p-left-{player-nick-name}
    // Room dropped: room-dropped
    // Game started: game-started
    // Creator transferred: creator-transferred-{new-owner-player-nick-name}
    // Settings changed: settings-changed
    public AsyncObservable<string> ChallengeChangedChannel { get; protected init; } = new();
}

public class AcceptedChallenge : Challenge
{
    private readonly bool _creatorIsWhite;
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