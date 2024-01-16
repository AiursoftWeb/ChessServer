using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;

namespace Aiursoft.ChessServer.Models.ViewModels;

public class DropChallengeViewModel
{
    [FromRoute]
    [Required]
    public int Id { get; set; }
    
    [FromForm]
    [Required]
    public Guid PlayerId { get; set; }
}