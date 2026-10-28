using Aiursoft.ChessServer.Models.ViewModels;
using Aiursoft.ChessServer.Services;
using Aiursoft.WebTools.Attributes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace Aiursoft.ChessServer.Controllers;

[LimitPerMin]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class GuestController(GuestIdentity guests, IStringLocalizer<GuestController> localizer) : Controller
{
    private string Destination(string? returnUrl) => Url.IsLocalUrl(returnUrl) &&
        !returnUrl.StartsWith("/Guest", StringComparison.OrdinalIgnoreCase) ? returnUrl : "/Lobby";

    [HttpGet]
    public IActionResult Index(string? returnUrl = null)
    {
        var destination = Destination(returnUrl);
        if (User.Identity?.IsAuthenticated == true || guests.Read(HttpContext) != null) return LocalRedirect(destination);
        return this.StackView(new GuestViewModel { ReturnUrl = destination });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult Index(GuestViewModel model)
    {
        model.ReturnUrl = Destination(model.ReturnUrl);
        if (User.Identity?.IsAuthenticated == true || guests.Read(HttpContext) != null) return LocalRedirect(model.ReturnUrl);
        var name = model.NickName?.Trim() ?? "";
        if (string.IsNullOrEmpty(name) || name.Length > 32 || name.Any(char.IsControl))
            ModelState.AddModelError(nameof(model.NickName), localizer["Enter a nickname of 1 to 32 characters without control characters."]);
        if (!ModelState.IsValid) return this.StackView(model);
        guests.Create(HttpContext, name);
        return LocalRedirect(model.ReturnUrl);
    }
}
