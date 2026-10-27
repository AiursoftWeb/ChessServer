using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.ChessServer.Entities;

[Index(nameof(WhiteUserId), nameof(FinishedAt))]
[Index(nameof(BlackUserId), nameof(FinishedAt))]
public class CompletedGame
{
    [Key] public Guid Id { get; set; }
    [MaxLength(450)] public required string WhiteUserId { get; set; }
    [MaxLength(450)] public required string BlackUserId { get; set; }
    [MaxLength(256)] public required string WhiteName { get; set; }
    [MaxLength(256)] public required string BlackName { get; set; }
    [MaxLength(16)] public required string Result { get; set; }
    [MaxLength(32)] public required string EndReason { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime FinishedAt { get; set; }
    // ReSharper disable once EntityFramework.ModelValidation.UnlimitedStringLength
    public required string Pgn { get; set; }
}
