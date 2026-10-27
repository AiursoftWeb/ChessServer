using System.Security.Claims;
using Aiursoft.ChessServer.Services.Authentication;
using Aiursoft.ChessServer.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aiursoft.ChessServer.Controllers;

[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public abstract class ChessController : Controller
{
    protected Guid PlayerId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    protected Player CurrentPlayer() => new(PlayerId) { NickName = User.FindFirstValue(UserClaimsPrincipalFactory.DisplayNameClaimType) ?? User.Identity!.Name ?? "Player" };

    protected bool IsSameOriginWebSocket()
    {
        var origin = Request.Headers.Origin.ToString();
        return Uri.TryCreate(origin, UriKind.Absolute, out var uri) &&
               uri.GetLeftPart(UriPartial.Authority).Equals($"{Request.Scheme}://{Request.Host}", StringComparison.OrdinalIgnoreCase);
    }
}
