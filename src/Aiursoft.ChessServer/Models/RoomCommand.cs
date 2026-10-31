using System.ComponentModel.DataAnnotations;

namespace Aiursoft.ChessServer.Models;

public class RoomCommand
{
    public Guid CommandId { get; set; }
    public long Version { get; set; }
    [Required, StringLength(24)] public string Kind { get; set; } = "";
    public Guid? Target { get; set; }
    [StringLength(12)] public string? Seat { get; set; }
    [StringLength(16)] public string? Ai { get; set; }
    public int Difficulty { get; set; } = 1;
    [StringLength(256)] public string? Model { get; set; }
    [StringLength(80)] public string? Name { get; set; }
    public int Minutes { get; set; }
    public int IncrementSeconds { get; set; }
    public bool Public { get; set; }
    public bool RandomColors { get; set; }
    public bool Confirm { get; set; }
}
