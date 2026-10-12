using Microsoft.AspNetCore.Antiforgery;

namespace FransStudios.Api.Infrastructure;

public static class SecurityHeadersMiddleware
{
    // The SPA is a static Vite build: no inline scripts, so script-src can stay strict.
    // style-src allows inline style attributes set by React (CSSOM writes are not blocked anyway).
    private const string Csp =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; " +
        "media-src 'self' blob:; font-src 'self'; connect-src 'self' https://www.googleapis.com; frame-src 'none'; object-src 'none'; " +
        "base-uri 'self'; form-action 'self'; frame-ancestors 'none'";

    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) => app.Use(async (ctx, next) =>
    {
        ctx.Response.OnStarting(() =>
        {
            var h = ctx.Response.Headers;
            h.XContentTypeOptions = "nosniff";
            h["Referrer-Policy"] = "strict-origin-when-cross-origin";
            h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
            h["Cross-Origin-Opener-Policy"] = "same-origin";
            if (!h.ContainsKey("Content-Security-Policy")) h.ContentSecurityPolicy = Csp;
            h.XFrameOptions = "DENY";
            h.Remove("X-Powered-By");
            h.Remove("Server");
            return Task.CompletedTask;
        });
        await next();
    });
}

/// <summary>
/// CSRF protection for the cookie-authenticated API: every unsafe /api request must carry the
/// X-XSRF-TOKEN header matching the antiforgery cookie. Combined with SameSite=Strict cookies.
/// </summary>
public static class AntiforgeryMiddleware
{
    public static IApplicationBuilder UseApiAntiforgery(this IApplicationBuilder app) => app.Use(async (ctx, next) =>
    {
        if (ctx.Request.Path.StartsWithSegments("/api") && !HttpMethods.IsGet(ctx.Request.Method)
            && !HttpMethods.IsHead(ctx.Request.Method) && !HttpMethods.IsOptions(ctx.Request.Method))
        {
            var antiforgery = ctx.RequestServices.GetRequiredService<IAntiforgery>();
            if (!await antiforgery.IsRequestValidAsync(ctx))
            {
                await Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid or missing CSRF token.",
                    detail: "Reload the page and try again.").ExecuteAsync(ctx);
                return;
            }
        }
        await next();
    });

    /// <summary>Issues a fresh request token in the readable XSRF-TOKEN cookie for the SPA to echo back.</summary>
    public static void IssueToken(HttpContext ctx)
    {
        var antiforgery = ctx.RequestServices.GetRequiredService<IAntiforgery>();
        var tokens = antiforgery.GetAndStoreTokens(ctx);
        ctx.Response.Cookies.Append("XSRF-TOKEN", tokens.RequestToken!, new CookieOptions
        {
            HttpOnly = false, // the SPA must read it
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/",
        });
    }
}
