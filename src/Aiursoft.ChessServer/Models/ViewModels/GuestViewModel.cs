using System.ComponentModel.DataAnnotations;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.ChessServer.Models.ViewModels;

public class GuestViewModel : UiStackLayoutViewModel
{
    public GuestViewModel() { PageTitle = "Play as a guest"; }
    [Required, StringLength(32), Display(Name = "Your nickname")]
    public string? NickName { get; set; } = "";
    public string ReturnUrl { get; set; } = "/Lobby";
}
