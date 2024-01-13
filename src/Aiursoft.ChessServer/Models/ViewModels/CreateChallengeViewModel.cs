namespace Aiursoft.ChessServer.Models;

public class CreateChallengeViewModel
{
    public Guid CreatorId { get; set; }
    
    public string Message { get; set; } = "A chess room.";
    
    public RoleRule RoleRule { get; set; } = RoleRule.Random;
    
    public TimeSpan TimeLimit { get; set; } = TimeSpan.FromMinutes(10);
    
    public ChallengePermission Permission { get; set; } = ChallengePermission.Public;
}