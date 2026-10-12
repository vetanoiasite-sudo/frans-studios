using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using FransStudios.Api.Configuration;
using FransStudios.Api.Data;
using FransStudios.Api.Entities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace FransStudios.Api.GoogleDrive;

public sealed class DriveNotConnectedException(string message, string code = "not_connected") : Exception(message)
{
    /// <summary>Stable machine-readable code (safe to put in a redirect URL).</summary>
    public string Code { get; } = code;
}

public sealed class DriveApiException(int statusCode, string reason, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    /// <summary>Google error reason, e.g. "notFound", "downloadQuotaExceeded", "userRateLimitExceeded".</summary>
    public string Reason { get; } = reason;
}

/// <summary>
/// Owns the studio's Google OAuth credentials: code exchange, encrypted refresh-token storage,
/// access-token refresh and revocation. Nothing here is ever returned to the browser.
/// </summary>
public sealed class DriveTokenService(
    AppDbContext db,
    IHttpClientFactory httpFactory,
    IDataProtectionProvider dataProtection,
    IMemoryCache cache,
    IOptions<GoogleDriveOptions> options,
    TimeProvider clock,
    ILogger<DriveTokenService> logger)
{
    public const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    public const string AuthEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    public const string RevokeEndpoint = "https://oauth2.googleapis.com/revoke";
    private const string AccessTokenCacheKey = "google-drive:access-token";

    private readonly IDataProtector _protector = dataProtection.CreateProtector("FransStudios.GoogleDrive.RefreshToken.v1");

    public GoogleDriveOptions Options => options.Value;

    public Task<GoogleDriveConnection?> GetActiveConnectionAsync(CancellationToken ct) =>
        db.GoogleDriveConnections.AsNoTracking().SingleOrDefaultAsync(c => c.RevokedAt == null, ct);

    // ── Albums on a previously connected account ────────────────────────────
    //
    // Connecting another Google account keeps the previous account's refresh token (only "Disconnect" revokes
    // it), so albums whose folders are in that Drive keep working until they are moved to the new one.
    // DriveClient.ForAccount(id) makes Drive calls as such an account instead of the connected one.

    /// <summary>From <see cref="AccountIdForAsync"/>: the account's access is gone (it was disconnected).</summary>
    public const int UnavailableAccount = 0;

    /// <summary>
    /// The connection to use for a folder in <paramref name="email"/>'s Drive: null for the connected account
    /// (or an album from before accounts were recorded), else that account's latest stored connection, or
    /// <see cref="UnavailableAccount"/> if it was disconnected.
    /// </summary>
    public async Task<int?> AccountIdForAsync(string? email, CancellationToken ct)
    {
        if (email is null) return null;
        var active = await GetActiveConnectionAsync(ct);
        if (active is not null && SameAccount(active.AccountEmail, email)) return null;
        return await db.GoogleDriveConnections.AsNoTracking()
            .Where(c => c.AccountEmail == email && c.EncryptedRefreshToken != "")
            .OrderByDescending(c => c.Id).Select(c => (int?)c.Id).FirstOrDefaultAsync(ct) ?? UnavailableAccount;
    }

    public static bool SameAccount(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether the website can reach <paramref name="email"/>'s Drive: the connected account, or a previous one whose
    /// stored access Google still accepts (asked now, so an expired or removed access shows right away; cheap while
    /// an access token is cached, about an hour). A network problem counts as reachable.
    /// </summary>
    public async Task<bool> CanReachAsync(string email, CancellationToken ct) =>
        await AccountIdForAsync(email, ct) switch
        {
            null => true,
            UnavailableAccount => false,
            { } id => await CheckAccountAsync(id, ct),
        };

    private async Task<bool> CheckAccountAsync(int accountId, CancellationToken ct)
    {
        try
        {
            await GetAccessTokenAsync(accountId, ct);
            return true;
        }
        catch (DriveNotConnectedException) { return false; } // and GetAccessTokenAsync forgot the access
        catch (Exception ex) when (ex is DriveApiException or HttpRequestException) { return true; }
    }

    // ── OAuth authorization-code flow with PKCE ─────────────────────────────

    public static (string Verifier, string Challenge) CreatePkce()
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        return (verifier, challenge);
    }

    public string BuildAuthorizationUrl(string redirectUri, string state, string codeChallenge) =>
        AuthEndpoint + QueryString.Create(new Dictionary<string, string?>
        {
            ["client_id"] = Options.ClientId,
            ["redirect_uri"] = redirectUri,
            ["response_type"] = "code",
            ["scope"] = Options.Scopes,
            ["access_type"] = "offline",          // we need a refresh token
            ["prompt"] = "consent",               // always return a refresh token, even on re-connect
            ["include_granted_scopes"] = "false",
            ["state"] = state,
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256",
        });

    public async Task<GoogleDriveConnection> CompleteAuthorizationAsync(string code, string codeVerifier, string redirectUri,
        int adminUserId, CancellationToken ct)
    {
        var token = await PostTokenAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["code_verifier"] = codeVerifier,
            ["redirect_uri"] = redirectUri,
            ["client_id"] = Options.ClientId,
            ["client_secret"] = Options.ClientSecret,
        }, ct);

        if (string.IsNullOrEmpty(token.RefreshToken))
            throw new DriveNotConnectedException("Google did not return a refresh token. Remove the app's access at myaccount.google.com/permissions and connect again.", "no_refresh_token");

        var grantedScopes = token.Scope ?? "";
        var scopes = grantedScopes.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (!scopes.Any(s => s.EndsWith("/auth/drive.readonly") || s.EndsWith("/auth/drive")))
            throw new DriveNotConnectedException("Google Drive access was not granted. Please tick the Google Drive permission on the consent screen.", "scope_missing");

        var email = await GetAccountEmailAsync(token.AccessToken, ct);
        var now = clock.GetUtcNow().UtcDateTime;

        // SQL Server is configured with retry-on-failure, which only allows a transaction when the whole unit
        // runs inside the execution strategy (so it can be replayed as one).
        var connection = await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear(); // a replay starts clean
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await db.GoogleDriveConnections.Where(c => c.RevokedAt == null)
                .ExecuteUpdateAsync(u => u.SetProperty(c => c.RevokedAt, now), ct);
            var created = new GoogleDriveConnection
            {
                AccountEmail = email,
                EncryptedRefreshToken = _protector.Protect(token.RefreshToken),
                Scopes = grantedScopes,
                ConnectedByUserId = adminUserId,
                ConnectedAt = now,
            };
            db.GoogleDriveConnections.Add(created);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return created;
        });

        cache.Set(AccessTokenCacheKey, new CachedToken(connection.Id, token.AccessToken),
            TimeSpan.FromSeconds(Math.Max(60, token.ExpiresIn - 120)));
        return connection;
    }

    public async Task DisconnectAsync(CancellationToken ct)
    {
        var connection = await db.GoogleDriveConnections.SingleOrDefaultAsync(c => c.RevokedAt == null, ct);
        if (connection is null) return;

        try
        {
            var refresh = _protector.Unprotect(connection.EncryptedRefreshToken);
            using var http = httpFactory.CreateClient(DriveHttp.ApiClient);
            using var resp = await http.PostAsync(RevokeEndpoint,
                new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = refresh }), ct);
            if (!resp.IsSuccessStatusCode)
                logger.LogWarning("Google token revocation returned {Status}; removing local credentials anyway.", (int)resp.StatusCode);
        }
        catch (Exception ex) when (ex is HttpRequestException or CryptographicException)
        {
            logger.LogWarning(ex, "Could not revoke Google token remotely; removing local credentials anyway.");
        }

        connection.RevokedAt = clock.GetUtcNow().UtcDateTime;
        connection.EncryptedRefreshToken = "";
        await db.SaveChangesAsync(ct);
        cache.Remove(AccessTokenCacheKey);
    }

    // ── Access tokens ───────────────────────────────────────────────────────

    private sealed record CachedToken(int ConnectionId, string AccessToken);
    private static readonly SemaphoreSlim RefreshLock = new(1, 1);

    /// <summary>
    /// Returns a valid access token, refreshing it (once, under a lock) when needed. <paramref name="accountId"/> is a
    /// connection from <see cref="AccountIdForAsync"/>; null for the connected account.
    /// </summary>
    public async Task<string> GetAccessTokenAsync(int? accountId, CancellationToken ct)
    {
        if (accountId == UnavailableAccount)
            throw new DriveNotConnectedException("This album is in the Drive of a Google account that is no longer connected. Connect that account again, or choose the album's folder in the current account.");
        var cacheKey = CacheKey(accountId);
        if (cache.TryGetValue(cacheKey, out CachedToken? cached)) return cached!.AccessToken;

        await RefreshLock.WaitAsync(ct);
        try
        {
            if (cache.TryGetValue(cacheKey, out cached)) return cached!.AccessToken;

            if (!Options.IsConfigured)
                throw new DriveNotConnectedException("Google Drive is not configured on the server.");
            var connection = accountId is { } id
                ? await db.GoogleDriveConnections.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id && c.EncryptedRefreshToken != "", ct)
                  ?? throw new DriveNotConnectedException("The Google account that holds this album is no longer connected.")
                : await GetActiveConnectionAsync(ct) ?? throw new DriveNotConnectedException("Google Drive is not connected.");

            string refreshToken;
            try { refreshToken = _protector.Unprotect(connection.EncryptedRefreshToken); }
            catch (CryptographicException)
            {
                // Happens if the Data Protection key ring was lost (e.g. new server without the keys folder).
                throw new DriveNotConnectedException("Stored Google credentials can no longer be decrypted. Please reconnect Google Drive.");
            }

            TokenResponse token;
            try
            {
                token = await PostTokenAsync(new Dictionary<string, string>
                {
                    ["grant_type"] = "refresh_token",
                    ["refresh_token"] = refreshToken,
                    ["client_id"] = Options.ClientId,
                    ["client_secret"] = Options.ClientSecret,
                }, ct);
            }
            catch (DriveApiException ex) when (ex.Reason == "invalid_grant")
            {
                // Token revoked by the user, password changed, or the OAuth app was left in "Testing" (7-day expiry).
                logger.LogError("Google refresh token for {Email} rejected (invalid_grant).", connection.AccountEmail);
                // A previous account's access is gone for good: forget it, so the admin sees its albums as unreachable.
                // (The connected account's is kept: the admin is told to reconnect, which replaces it.)
                if (connection.RevokedAt is not null)
                    await db.GoogleDriveConnections.Where(c => c.Id == connection.Id)
                        .ExecuteUpdateAsync(u => u.SetProperty(c => c.EncryptedRefreshToken, ""), CancellationToken.None);
                throw new DriveNotConnectedException(accountId is null
                    ? "Google Drive access has expired or was revoked. Please reconnect Google Drive."
                    : $"Access to {connection.AccountEmail}'s Drive has expired or was revoked. Connect that account again to reach (or move) its albums.");
            }

            cache.Set(cacheKey, new CachedToken(connection.Id, token.AccessToken),
                TimeSpan.FromSeconds(Math.Max(60, token.ExpiresIn - 120)));
            return token.AccessToken;
        }
        finally { RefreshLock.Release(); }
    }

    /// <summary>Drops the cached access token, e.g. after Google answered 401.</summary>
    public void InvalidateAccessToken(int? accountId) => cache.Remove(CacheKey(accountId));

    private static string CacheKey(int? accountId) => accountId is { } id ? $"{AccessTokenCacheKey}:{id}" : AccessTokenCacheKey;

    private async Task<TokenResponse> PostTokenAsync(Dictionary<string, string> form, CancellationToken ct)
    {
        using var http = httpFactory.CreateClient(DriveHttp.ApiClient);
        using var resp = await http.PostAsync(TokenEndpoint, new FormUrlEncodedContent(form), ct);
        if (!resp.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadFromJsonAsync<TokenError>(ct).ConfigureAwait(false) ?? new TokenError();
            logger.LogWarning("Google token endpoint returned {Status}: {Error}", (int)resp.StatusCode, err.Error);
            throw new DriveApiException((int)resp.StatusCode, err.Error ?? "token_error", "Google token request failed.");
        }
        return await resp.Content.ReadFromJsonAsync<TokenResponse>(ct) ?? throw new InvalidOperationException("Empty token response.");
    }

    private async Task<string> GetAccountEmailAsync(string accessToken, CancellationToken ct)
    {
        using var http = httpFactory.CreateClient(DriveHttp.ApiClient);
        using var req = new HttpRequestMessage(HttpMethod.Get, DriveHttp.ApiBase + "about?fields=user(emailAddress)");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var resp = await http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        var about = await resp.Content.ReadFromJsonAsync<AboutResponse>(ct);
        return about?.User?.EmailAddress ?? "unknown";
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")] public string AccessToken { get; set; } = "";
        [JsonPropertyName("refresh_token")] public string? RefreshToken { get; set; }
        [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
        [JsonPropertyName("scope")] public string? Scope { get; set; }
    }

    private sealed class TokenError
    {
        [JsonPropertyName("error")] public string? Error { get; set; }
    }

    private sealed class AboutResponse
    {
        public AboutUser? User { get; set; }
        public sealed class AboutUser { public string? EmailAddress { get; set; } }
    }
}
