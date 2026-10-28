using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Aiursoft.ChessServer.Models;
using Aiursoft.ChessServer.Services.Authentication;
using Aiursoft.Scanner.Abstractions;
using Microsoft.AspNetCore.DataProtection;

namespace Aiursoft.ChessServer.Services;

// A guest owns a browser seat, never an authenticated account or permission claim.
public class GuestIdentity(IDataProtectionProvider protection) : IScopedDependency
{
    public const string CookieName = "ChessServer.Guest";
    private readonly IDataProtector _protector = protection.CreateProtector("ChessServer.Guest.v1");
    private sealed record Ticket(Guid Id, string Name, DateTimeOffset Expires);

    public Player? Read(HttpContext context)
    {
        if (!context.Request.Cookies.TryGetValue(CookieName, out var value) || value.Length > 2048) return null;
        try
        {
            var ticket = JsonSerializer.Deserialize<Ticket>(_protector.Unprotect(value));
            return ticket == null || ticket.Id == Guid.Empty || ticket.Expires <= DateTimeOffset.UtcNow
                ? null : new Player(ticket.Id) { NickName = ticket.Name, IsGuest = true };
        }
        catch (CryptographicException) { return null; }
        catch (JsonException) { return null; }
    }

    public void Create(HttpContext context, string name)
    {
        var expires = DateTimeOffset.UtcNow.AddDays(30);
        var ticket = new Ticket(Guid.NewGuid(), name, expires);
        context.Response.Cookies.Append(CookieName, _protector.Protect(JsonSerializer.Serialize(ticket)),
            new CookieOptions { HttpOnly = true, Secure = context.Request.IsHttps, SameSite = SameSiteMode.Lax,
                Path = "/", IsEssential = true, Expires = expires });
    }

    public static Player? Registered(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated != true ||
            !Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)) return null;
        return new Player(id) { NickName = context.User.FindFirstValue(UserClaimsPrincipalFactory.DisplayNameClaimType)
            ?? context.User.Identity.Name ?? "Player" };
    }
}
