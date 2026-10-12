using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace FransStudios.Api.Authentication;

/// <summary>Re-validates the session behind every authenticated request and returns 401/403 instead of redirects.</summary>
public sealed class SessionCookieEvents(SessionService sessions) : CookieAuthenticationEvents
{
    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        var p = context.Principal;
        var ok = p is not null
                 && Guid.TryParse(p.FindFirstValue(AppClaims.SessionId), out var sid)
                 && int.TryParse(p.FindFirstValue(AppClaims.UserId), out var uid)
                 && Guid.TryParse(p.FindFirstValue(AppClaims.SecurityStamp), out var stamp)
                 && await sessions.IsValidAsync(sid, uid, stamp, ParseClientId(p), context.HttpContext.RequestAborted);

        if (!ok)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }
    }

    private static int? ParseClientId(ClaimsPrincipal p) =>
        int.TryParse(p.FindFirstValue(AppClaims.ClientId), out var id) ? id : null;

    public override Task RedirectToLogin(RedirectContext<CookieAuthenticationOptions> context)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    }

    public override Task RedirectToAccessDenied(RedirectContext<CookieAuthenticationOptions> context)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    }
}
