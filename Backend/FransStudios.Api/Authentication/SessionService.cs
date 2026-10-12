using FransStudios.Api.Configuration;
using FransStudios.Api.Data;
using FransStudios.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace FransStudios.Api.Authentication;

/// <summary>
/// Server-side sessions. The auth cookie only carries a session id; this service decides whether
/// that session is still allowed. Revoking a session (logout, password reset, client disabled)
/// takes effect on the next request in this process, and within <see cref="CacheFor"/> elsewhere.
/// </summary>
public sealed class SessionService(AppDbContext db, IMemoryCache cache, TimeProvider clock, IOptions<AuthOptions> options)
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan TouchEvery = TimeSpan.FromMinutes(5);

    private static string CacheKey(Guid sessionId) => $"session:{sessionId}";

    public async Task<Session> CreateAsync(User user, string? ip, string? userAgent, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var session = new Session
        {
            UserId = user.Id,
            CreatedAt = now,
            LastSeenAt = now,
            ExpiresAt = now + Lifetime(user.Role),
            IpAddress = ip,
            UserAgent = userAgent is { Length: > 400 } ? userAgent[..400] : userAgent,
        };
        db.Sessions.Add(session);
        await db.SaveChangesAsync(ct);
        return session;
    }

    public async Task<bool> IsValidAsync(Guid sessionId, int userId, Guid stamp, int? clientId, CancellationToken ct)
    {
        if (cache.TryGetValue(CacheKey(sessionId), out bool cached)) return cached;

        var now = clock.GetUtcNow().UtcDateTime;
        var row = await db.Sessions.IgnoreQueryFilters()
            .Where(s => s.Id == sessionId && s.UserId == userId)
            .Select(s => new
            {
                s.RevokedAt,
                s.ExpiresAt,
                s.LastSeenAt,
                s.User.IsActive,
                s.User.DeletedAt,
                s.User.SecurityStamp,
                s.User.Role,
                s.User.ClientId,
                ClientOk = s.User.Client == null || (s.User.Client.IsActive && s.User.Client.DeletedAt == null),
            })
            .SingleOrDefaultAsync(ct);

        var valid = row is not null
                    && row.RevokedAt is null
                    && row.ExpiresAt > now
                    && row.IsActive
                    && row.DeletedAt is null
                    && row.SecurityStamp == stamp
                    && row.ClientId == clientId
                    && row.ClientOk;

        if (valid && now - row!.LastSeenAt > TouchEvery)
        {
            // Client sessions slide; admin sessions keep their absolute expiry.
            var newExpiry = row.Role == UserRole.Client ? now + Lifetime(UserRole.Client) : row.ExpiresAt;
            await db.Sessions.Where(s => s.Id == sessionId)
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.LastSeenAt, now).SetProperty(s => s.ExpiresAt, newExpiry), ct);
        }

        if (valid) cache.Set(CacheKey(sessionId), true, CacheFor);
        return valid;
    }

    public async Task RevokeAsync(Guid sessionId, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        await db.Sessions.IgnoreQueryFilters().Where(s => s.Id == sessionId && s.RevokedAt == null)
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.RevokedAt, now), ct);
        cache.Remove(CacheKey(sessionId));
    }

    public Task RevokeAllForUserAsync(int userId, CancellationToken ct) =>
        RevokeWhereAsync(s => s.UserId == userId, ct);

    public Task RevokeAllForClientAsync(int clientId, CancellationToken ct) =>
        RevokeWhereAsync(s => s.User.ClientId == clientId, ct);

    private async Task RevokeWhereAsync(System.Linq.Expressions.Expression<Func<Session, bool>> predicate, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var active = db.Sessions.IgnoreQueryFilters().Where(predicate).Where(s => s.RevokedAt == null);
        var ids = await active.Select(s => s.Id).ToListAsync(ct);
        if (ids.Count == 0) return;
        await db.Sessions.IgnoreQueryFilters().Where(s => ids.Contains(s.Id))
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.RevokedAt, now), ct);
        foreach (var id in ids) cache.Remove(CacheKey(id));
    }

    public TimeSpan Lifetime(UserRole role) => role == UserRole.Admin
        ? TimeSpan.FromHours(options.Value.AdminSessionHours)
        : TimeSpan.FromDays(options.Value.ClientSessionDays);
}
