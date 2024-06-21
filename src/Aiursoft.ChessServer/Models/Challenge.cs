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