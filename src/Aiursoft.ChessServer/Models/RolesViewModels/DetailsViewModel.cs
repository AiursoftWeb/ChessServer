using System.ComponentModel.DataAnnotations;
using Aiursoft.ChessServer.Authorization;
using Aiursoft.ChessServer.Entities;
using Aiursoft.UiStack.Layout;
using Microsoft.AspNetCore.Identity;

namespace Aiursoft.ChessServer.Models.RolesViewModels;

public class DetailsViewModel : UiStackLayoutViewModel
{
    public DetailsViewModel()
    {
        PageTitle = "Role Details";
    }

    [Display(Name = "Role")]
    public required IdentityRole Role { get; set; }

    [Display(Name = "Permissions")]
    public required List<PermissionDescriptor> Permissions { get; set; }

    [Display(Name = "Users in role")]
    public required IList<User> UsersInRole { get; set; }
}
