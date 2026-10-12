using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using FransStudios.Api.Authentication;
using FransStudios.Api.Configuration;
using FransStudios.Api.Data;
using FransStudios.Api.Entities;
using FransStudios.Api.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FransStudios.Api.Features.Auth;

public sealed record LoginRequest(
    [Required, StringLength(64)] string Username,
    [Required, StringLength(128)] string Password);

public sealed record ChangePasswordRequest(
    [Required, StringLength(128)] string CurrentPassword,
    [Required, StringLength(128)] string NewPassword);

public sealed record MeResponse(string Username, string Role, string DisplayName);

[ApiController]
[Route("api/auth")]
public sealed class AuthController(
    AppDbContext db,
    PasswordService passwords,
    SessionService sessions,
    AuditService audit,
    CurrentUser currentUser,
    TimeProvider clock,
    IOptions<AuthOptions> authOptions) : ControllerBase
{
    private const string InvalidCredentials = "Invalid username or password.";

    /// <summary>Issues the antiforgery token the SPA must send with every POST/PUT/DELETE.</summary>
    [HttpGet("csrf")]
    public IActionResult Csrf()
    {
        AntiforgeryMiddleware.IssueToken(HttpContext);
        return NoContent();
    }

    [HttpPost("login")]
    [EnableRateLimiting(RateLimits.Login)]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken ct)
    {
        var username = NormalizeUsername(request.Username);
        var now = clock.GetUtcNow().UtcDateTime;
        var options = authOptions.Value;

        var user = await db.Users.Include(u => u.Client).SingleOrDefaultAsync(u => u.Username == username, ct);

        if (user?.LockoutEndUtc > now)
        {
            await audit.WriteAsync(AuditActions.LoginLockedOut, "User", user.Id, userId: user.Id, ct: ct);
            return Problem(statusCode: 401, title: InvalidCredentials,
                detail: "Too many failed attempts. Please wait a few minutes and try again.");
        }

        var (valid, needsRehash) = passwords.Verify(user, request.Password);
        if (!valid)
        {
            if (user is not null)
            {
                user.FailedLoginCount++;
                if (user.FailedLoginCount >= options.MaxFailedLogins)
                {
                    user.LockoutEndUtc = now.AddMinutes(options.LockoutMinutes);
                    user.FailedLoginCount = 0;
                }
                audit.Add(AuditActions.LoginFailed, "User", user.Id, userId: user.Id);
                await db.SaveChangesAsync(ct);
            }
            else
            {
                await audit.WriteAsync(AuditActions.LoginFailed, details: new { username }, ct: ct);
            }
            return Problem(statusCode: 401, title: InvalidCredentials);
        }

        // Only someone holding the correct password learns that the account is disabled.
        // (A soft-deleted client is filtered out of the Include, so a client login without a Client is disabled too.)
        if (!user!.IsActive || (user.Role == UserRole.Client && user.Client is not { IsActive: true }))
            return Problem(statusCode: 401, title: "This account is disabled. Please contact the studio.");

        user.FailedLoginCount = 0;
        user.LockoutEndUtc = null;
        user.LastLoginAt = now;
        if (needsRehash) user.PasswordHash = passwords.Hash(user, request.Password);
        audit.Add(AuditActions.LoginSuccess, "User", user.Id, userId: user.Id);
        await db.SaveChangesAsync(ct);

        await SignInAsync(user, ct);
        return Ok(ToMe(user));
    }

    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        await sessions.RevokeAsync(currentUser.SessionId, ct);
        await audit.WriteAsync(AuditActions.Logout, "User", currentUser.UserId, ct: ct);
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity());
        AntiforgeryMiddleware.IssueToken(HttpContext);
        return NoContent();
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<MeResponse>> Me(CancellationToken ct)
    {
        var user = await db.Users.Include(u => u.Client).SingleAsync(u => u.Id == currentUser.UserId, ct);
        return ToMe(user);
    }

    [Authorize]
    [HttpPost("change-password")]
    [EnableRateLimiting(RateLimits.Login)]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken ct)
    {
        var user = await db.Users.Include(u => u.Client).SingleAsync(u => u.Id == currentUser.UserId, ct);
        if (!passwords.Verify(user, request.CurrentPassword).Valid)
            return ValidationProblem(Errors(nameof(request.CurrentPassword), "Current password is incorrect."));
        if (passwords.Validate(request.NewPassword) is { } error)
            return ValidationProblem(Errors(nameof(request.NewPassword), error));

        user.PasswordHash = passwords.Hash(user, request.NewPassword);
        user.SecurityStamp = Guid.NewGuid();
        audit.Add(AuditActions.PasswordChanged, "User", user.Id);
        await db.SaveChangesAsync(ct);

        // Sign out every other device, then start a fresh session here.
        await sessions.RevokeAllForUserAsync(user.Id, ct);
        await SignInAsync(user, ct);
        return NoContent();
    }

    private async Task SignInAsync(User user, CancellationToken ct)
    {
        var session = await sessions.CreateAsync(user, HttpContext.Connection.RemoteIpAddress?.ToString(),
            Request.Headers.UserAgent.ToString(), ct);

        var claims = new List<Claim>
        {
            new(AppClaims.UserId, user.Id.ToString()),
            new(AppClaims.SessionId, session.Id.ToString()),
            new(AppClaims.SecurityStamp, user.SecurityStamp.ToString()),
            new(AppClaims.Role, user.Role.ToString()),
            new(AppClaims.Name, user.Username),
        };
        if (user.ClientId is { } clientId) claims.Add(new Claim(AppClaims.ClientId, clientId.ToString()));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal,
            new AuthenticationProperties { IsPersistent = true, ExpiresUtc = session.ExpiresAt, AllowRefresh = true });

        // The antiforgery token is bound to the identity, so issue a new one for the signed-in user.
        HttpContext.User = principal;
        AntiforgeryMiddleware.IssueToken(HttpContext);
    }

    private static MeResponse ToMe(User user) => new(
        user.Username,
        user.Role == UserRole.Admin ? "admin" : "client",
        user.Client?.FullName ?? user.Username);

    public static string NormalizeUsername(string username) => username.Trim().ToLowerInvariant();

    private static Dictionary<string, string[]> Errors(string field, string message) =>
        new() { [char.ToLowerInvariant(field[0]) + field[1..]] = [message] };

    private ActionResult ValidationProblem(Dictionary<string, string[]> errors) =>
        ValidationProblem(new ValidationProblemDetails(errors));
}
