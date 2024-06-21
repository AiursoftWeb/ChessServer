using Aiursoft.AiurObserver;

namespace Aiursoft.ChessServer.Models;

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