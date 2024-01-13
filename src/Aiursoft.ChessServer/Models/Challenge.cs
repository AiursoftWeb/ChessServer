using Aiursoft.AiurObserver;

namespace Aiursoft.ChessServer.Models;

public class Challenge
{
    public Challenge(Player creator)
    {
        Creator = creator;
    }

    public Player Creator { get; set; }
    public string Message { get; set; } = "A chess room.";

    public Player? Accepter { get; set; } = null;
    
    public Game? Game { get; set; } = null;
    
    public RoleRule RoleRule { get; set; } = RoleRule.Random;
    
    public TimeSpan TimeLimit { get; set; } = TimeSpan.FromMinutes(10);
    
    public ChallengePermission Permission { get; set; } = ChallengePermission.Public;
    
    // Possible messages:
    // Player joined: p-joined-{player-nick-name}
    // Player left: p-left-{player-nick-name}
    // Room dropped: room-dropped
    // Game started: game-started
    // Creator transferred: creator-transferred-{new-owner-player-nick-name}
    // Settings changed: settings-changed
    public AsyncObservable<string> ChallengeChangedChannel { get; } = new();
}