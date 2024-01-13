using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace Aiursoft.ChessServer.Attributes;

public class ValidNickName : ValidationAttribute
{
    private readonly string _keyWordRegex = "^[-a-zA-Z0-9_]+$";

    public override bool IsValid(object? value)
    {
        var regex = new Regex(_keyWordRegex, RegexOptions.Compiled);
        return value is string input && regex.IsMatch(input);
    }

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        return this.IsValid(value) ? ValidationResult.Success : new ValidationResult("The " + validationContext.DisplayName + " can only contain numbers, alphabet and underline.");
    }
}