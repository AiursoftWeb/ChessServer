using System.ComponentModel.DataAnnotations;
using Aiursoft.ChessServer.Entities;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.ChessServer.Models.UsersViewModels;

public class DeleteViewModel : UiStackLayoutViewModel
{
    public DeleteViewModel()
    {
        PageTitle = "Delete User";
    }

    [Display(Name = "User")]
    public required User User { get; set; }
}
