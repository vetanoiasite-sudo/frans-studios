using System.Security.Cryptography;
using System.Text.Json;
using FransStudios.Api.Authentication;
using FransStudios.Api.Configuration;
using FransStudios.Api.Data;
using FransStudios.Api.Entities;
using FransStudios.Api.GoogleDrive;
using FransStudios.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FransStudios.Api.Features.Admin;

/// <param name="CanUpload">Connected with full Drive access (older read-only connections must reconnect to upload).</param>
/// <param name="OtherAccounts">Previously connected accounts whose Drive still holds albums.</param>
public sealed record DriveStatus(bool Configured, bool Connected, string? AccountEmail, DateTime? ConnectedAt, string RedirectUri,
    bool CanUpload, IReadOnlyList<OtherDriveAccount> OtherAccounts);

/// <param name="Available">Its access is still stored, so its albums keep working and can be moved.</param>
public sealed record OtherDriveAccount(string Email, bool Available, int Albums, int Moving, int Failed);

public sealed record DriveFolderDto(string Id, string Name, DateTime? ModifiedTime);

[ApiController]
[Route("api/admin/google-drive")]
[Authorize(Policy = Policies.Admin)]
public sealed class AdminDriveController(
    AppDbContext db,
    DriveTokenService tokens,
    DriveClient drive,
    AuditService audit,
    CurrentUser currentUser,
    IDataProtectionProvider dataProtection,
    IOptions<SiteOptions> site,
    TimeProvider clock,
    ILogger<AdminDriveController> logger) : ControllerBase
{
    private const string StateCookie = "__Host-fs_goauth";
    private IDataProtector StateProtector => dataProtection.CreateProtector("FransStudios.GoogleDrive.OAuthState.v1");
    private string RedirectUri => site.Value.PublicBaseUrl.TrimEnd('/') + "/api/admin/google-drive/callback";

    private sealed record OAuthState(string State, string Verifier, int UserId, DateTime ExpiresAt);

    [HttpGet("status")]
    public async Task<DriveStatus> Status(CancellationToken ct)
    {
        var c = await tokens.GetActiveConnectionAsync(ct);
        var others = new List<OtherDriveAccount>();
        if (c is not null)
        {
            var groups = await db.Albums.AsNoTracking()
                .Where(a => a.DriveFolderId != null && a.DriveAccountEmail != null && a.DriveAccountEmail != c.AccountEmail)
                .GroupBy(a => a.DriveAccountEmail!)
                .Select(g => new
                {
                    Email = g.Key,
                    Albums = g.Count(),
                    Moving = g.Count(a => a.Move != null && a.Move.State != AlbumMoveState.Failed),
                    Failed = g.Count(a => a.Move != null && a.Move.State == AlbumMoveState.Failed),
                })
                .ToListAsync(ct);
            foreach (var g in groups)
                others.Add(new OtherDriveAccount(g.Email, await tokens.CanReachAsync(g.Email, ct), g.Albums, g.Moving, g.Failed));
        }
        return new DriveStatus(tokens.Options.IsConfigured, c is not null, c?.AccountEmail, c?.ConnectedAt, RedirectUri,
            c is not null && GoogleDriveOptions.CanWrite(c.Scopes), others);
    }

    /// <summary>Starts the Google consent flow (top-level navigation from the admin UI).</summary>
    [HttpGet("connect")]
    public IActionResult Connect()
    {
        if (!tokens.Options.IsConfigured)
            return Problem(statusCode: 503, title: "Google OAuth is not configured on the server (GoogleDrive:ClientId / ClientSecret).");

        var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        var (verifier, challenge) = DriveTokenService.CreatePkce();
        var payload = new OAuthState(state, verifier, currentUser.UserId, clock.GetUtcNow().UtcDateTime.AddMinutes(10));

        // SameSite=Lax: this cookie must survive the top-level redirect back from accounts.google.com.
        Response.Cookies.Append(StateCookie, StateProtector.Protect(JsonSerializer.Serialize(payload)), new CookieOptions
        {
            HttpOnly = true, Secure = true, SameSite = SameSiteMode.Lax, Path = "/", MaxAge = TimeSpan.FromMinutes(10),
        });
        return Redirect(tokens.BuildAuthorizationUrl(RedirectUri, state, challenge));
    }

    /// <summary>
    /// Google redirects here. The SameSite=Strict auth cookie is not sent on this cross-site navigation,
    /// so the request is authenticated by the encrypted, single-use state cookie instead (which records
    /// the admin who started the flow and is re-checked against the database).
    /// </summary>
    [AllowAnonymous]
    [HttpGet("callback")]
    public async Task<IActionResult> Callback(string? code, string? state, string? error, CancellationToken ct)
    {
        const string page = "/admin/google-drive";
        var raw = Request.Cookies[StateCookie];
        Response.Cookies.Delete(StateCookie, new CookieOptions { Secure = true, Path = "/", SameSite = SameSiteMode.Lax });

        if (!string.IsNullOrEmpty(error)) return Redirect($"{page}?error=consent_denied");
        if (raw is null || code is null || state is null) return Redirect($"{page}?error=invalid_state");

        OAuthState? saved;
        try { saved = JsonSerializer.Deserialize<OAuthState>(StateProtector.Unprotect(raw)); }
        catch (CryptographicException) { return Redirect($"{page}?error=invalid_state"); }

        if (saved is null || saved.ExpiresAt < clock.GetUtcNow().UtcDateTime
                          || !CryptographicOperations.FixedTimeEquals(System.Text.Encoding.ASCII.GetBytes(saved.State), System.Text.Encoding.ASCII.GetBytes(state)))
            return Redirect($"{page}?error=invalid_state");

        var isAdmin = await db.Users.AnyAsync(u => u.Id == saved.UserId && u.Role == UserRole.Admin && u.IsActive, ct);
        if (!isAdmin) return Redirect($"{page}?error=invalid_state");

        try
        {
            var connection = await tokens.CompleteAuthorizationAsync(code, saved.Verifier, RedirectUri, saved.UserId, ct);
            await audit.WriteAsync(AuditActions.DriveConnected, "GoogleDriveConnection", connection.Id,
                new { connection.AccountEmail }, userId: saved.UserId, ct: ct);
            return Redirect($"{page}?connected=1");
        }
        catch (DriveNotConnectedException ex)
        {
            logger.LogWarning("Drive connect failed: {Message}", ex.Message);
            return Redirect($"{page}?error={ex.Code}");
        }
        catch (DriveApiException ex)
        {
            logger.LogWarning(ex, "Drive connect failed");
            return Redirect($"{page}?error=token_exchange_failed");
        }
    }

    [HttpPost("disconnect")]
    public async Task<IActionResult> Disconnect(CancellationToken ct)
    {
        await tokens.DisconnectAsync(ct);
        await audit.WriteAsync(AuditActions.DriveDisconnected, ct: ct);
        return NoContent();
    }

    /// <summary>Folder browser for the album folder picker. parentId defaults to "root" (My Drive).</summary>
    [HttpGet("folders")]
    public async Task<ActionResult<IReadOnlyList<DriveFolderDto>>> Folders(string? parentId, string? q, CancellationToken ct)
    {
        parentId ??= "root";
        if (!DriveClient.IsValidId(parentId)) return this.FieldError("parentId", "Invalid folder id.");
        if (q is { Length: > 100 }) return this.FieldError("q", "Search is too long.");
        try
        {
            var folders = await drive.ListFoldersAsync(parentId, q, ct);
            return folders.Select(f => new DriveFolderDto(f.Id, f.Name, f.ModifiedTime)).ToList();
        }
        catch (DriveNotConnectedException ex) { return Problem(statusCode: 503, title: ex.Message); }
        catch (DriveApiException ex) when (ex.StatusCode == 404) { return this.NotFoundProblem("Folder not found."); }
    }

    /// <summary>Breadcrumb path for a folder in the picker.</summary>
    [HttpGet("folders/{id}/path")]
    public async Task<ActionResult<object>> FolderPath(string id, CancellationToken ct)
    {
        if (!DriveClient.IsValidId(id)) return this.FieldError("id", "Invalid folder id.");
        try { return new { path = await drive.GetFolderPathAsync(id, ct) }; }
        catch (DriveNotConnectedException ex) { return Problem(statusCode: 503, title: ex.Message); }
        catch (DriveApiException ex) when (ex.StatusCode == 404) { return this.NotFoundProblem("Folder not found."); }
    }
}
