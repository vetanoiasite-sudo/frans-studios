using System.Security.Claims;
using FransStudios.Api.Entities;

namespace FransStudios.Api.Authentication;

public static class AppClaims
{
    public const string UserId = "uid";
    public const string SessionId = "sid";
    public const string SecurityStamp = "stamp";
    public const string ClientId = "cid";
    public const string Role = ClaimTypes.Role;
    public const string Name = ClaimTypes.Name;
}

public static class Policies
{
    public const string Admin = "Admin";
    public const string Client = "Client";
}

/// <summary>
/// The authenticated caller, read from the encrypted auth cookie (already re-validated against the
/// database by <see cref="SessionCookieEvents"/>). This is the ONLY source of user/client identity —
/// never route values, query strings or request bodies.
/// </summary>
public sealed class CurrentUser(IHttpContextAccessor accessor)
{
    private ClaimsPrincipal Principal => accessor.HttpContext?.User ?? new ClaimsPrincipal();

    public bool IsAuthenticated => Principal.Identity?.IsAuthenticated == true;

    public int UserId => int.Parse(Required(AppClaims.UserId));

    public Guid SessionId => Guid.Parse(Required(AppClaims.SessionId));

    public UserRole Role => Enum.Parse<UserRole>(Required(AppClaims.Role));

    /// <summary>The caller's client id. Throws for non-client callers so it can't be misused on admin routes.</summary>
    public int ClientId =>
        int.TryParse(Principal.FindFirstValue(AppClaims.ClientId), out var id) && Role == UserRole.Client
            ? id
            : throw new InvalidOperationException("Current user is not a client.");

    public int? UserIdOrNull => int.TryParse(Principal.FindFirstValue(AppClaims.UserId), out var id) ? id : null;

    private string Required(string type) =>
        Principal.FindFirstValue(type) ?? throw new InvalidOperationException($"Missing claim '{type}'.");
}
