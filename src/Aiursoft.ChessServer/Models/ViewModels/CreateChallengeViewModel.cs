using System.ComponentModel.DataAnnotations;

namespace Aiursoft.ChessServer.Models;

public class CreateChallengeViewModel
{
    [Required]
    public Guid CreatorId { get; init; }
    
    [Required]
    [MinLength(3)]
    [MaxLength(20)]
    public string Message { get; init; } = "A chess room.";
    
    [Required]
    public RoleRule RoleRule { get; init; } = RoleRule.Random;
    
    [Required]
    public TimeSpan TimeLimit { get; init; } = TimeSpan.FromMinutes(10);
    
    [Required]
    public ChallengePermission Permission { get; init; } = ChallengePermission.Public;
}