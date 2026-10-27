using System.ComponentModel.DataAnnotations;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.ChessServer.Models.ViewModels;

public class CreateChallengeViewModel : UiStackLayoutViewModel
{
    public CreateChallengeViewModel()
    {
        PageTitle = "Create a challenge";
    }
    [Required, StringLength(80, MinimumLength = 3)]
    public string Message { get; set; } = "A chess game";
    [EnumDataType(typeof(RoleRule))]
    public RoleRule RoleRule { get; set; } = RoleRule.Random;
    [EnumDataType(typeof(ChallengePermission))]
    public ChallengePermission Permission { get; set; } = ChallengePermission.Public;
    [Range(1, 120)]
    public int Minutes { get; set; } = 10;
}
