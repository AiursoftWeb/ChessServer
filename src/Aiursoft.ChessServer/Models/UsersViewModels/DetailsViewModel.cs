using System.ComponentModel.DataAnnotations;
using Aiursoft.ChessServer.Authorization;
using Aiursoft.ChessServer.Entities;
using Aiursoft.UiStack.Layout;
using Microsoft.AspNetCore.Identity;

namespace Aiursoft.ChessServer.Models.UsersViewModels;

public class DetailsViewModel : UiStackLayoutViewModel
{
    public DetailsViewModel()
    {
        PageTitle = "User Details";
    }

    [Display(Name = "User")]
    public required User User { get; set; }

    [Display(Name = "Roles")]
    public required IList<IdentityRole> Roles { get; set; }

    [Display(Name = "Permissions")]
    public required List<PermissionDescriptor> Permissions { get; set; }
}
