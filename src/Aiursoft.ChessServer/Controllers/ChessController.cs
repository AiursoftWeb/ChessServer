using Aiursoft.ChessServer.Services;
using Microsoft.AspNetCore.Mvc.Filters;
using Aiursoft.ChessServer.Models;
using Microsoft.AspNetCore.Mvc;

namespace Aiursoft.ChessServer.Controllers;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public abstract class ChessController : Controller
{
    private Player _player = null!;
    protected Guid PlayerId => _player.Id;
    protected Player CurrentPlayer() => _player;

    public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var guests = HttpContext.RequestServices.GetRequiredService<GuestIdentity>();
        var guest = guests.Read(HttpContext);
        var registered = GuestIdentity.Registered(HttpContext);
        if (registered != null && guest != null)
        {
            try { await HttpContext.RequestServices.GetRequiredService<GameSessionService>().ClaimGuestAsync(guest.Id, registered); }
            catch (InvalidOperationException exception) { context.Result = Conflict(exception.Message); return; }
            Response.Cookies.Delete(GuestIdentity.CookieName, new CookieOptions { Path = "/" });
        }
        var player = registered ?? guest;
        if (player == null)
        {
            context.Result = Request.Path.Value?.EndsWith(".ws", StringComparison.OrdinalIgnoreCase) == true
                ? new UnauthorizedResult()
                : RedirectToAction("Index", "Guest", new { returnUrl = Request.Path + Request.QueryString });
            return;
        }
        _player = player;
        await next();
    }

    protected bool IsSameOriginWebSocket()
    {
        var origin = Request.Headers.Origin.ToString();
        return Uri.TryCreate(origin, UriKind.Absolute, out var uri) &&
               uri.GetLeftPart(UriPartial.Authority).Equals($"{Request.Scheme}://{Request.Host}", StringComparison.OrdinalIgnoreCase);
    }
}
