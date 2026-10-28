using System.ComponentModel.DataAnnotations;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.ChessServer.Models.ViewModels;

public class CreateChallengeViewModel : UiStackLayoutViewModel, IValidatableObject
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
    [AllowedValues(0, 3, 5, 10, 15)]
    public int Minutes { get; set; } = 10;
    [AllowedValues(0, 1, 2, 3, 5, 10, 15, 30)]
    public int IncrementSeconds { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Minutes == 0 && IncrementSeconds != 0)
            yield return new ValidationResult("Unlimited games cannot have a time increment.", [nameof(IncrementSeconds)]);
    }
}
