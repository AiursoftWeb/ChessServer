using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Aiursoft.ChessServer.Entities;

public class GameSession
{
    [Key, DatabaseGenerated(DatabaseGeneratedOption.None)] public int Id { get; set; }
    // Null retains the room number after the expired session has been removed.
    // ReSharper disable once EntityFramework.ModelValidation.UnlimitedStringLength
    public string? State { get; set; }
}
