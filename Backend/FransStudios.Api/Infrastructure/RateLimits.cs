using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace FransStudios.Api.Infrastructure;

public static class RateLimits
{
    public const string Login = "login";
    public const string Inquiry = "inquiry";

    public static IServiceCollection AddAppRateLimiting(this IServiceCollection services, IConfiguration config) => services.AddRateLimiter(o =>
    {
        var loginPerMinute = config.GetValue("RateLimits:LoginPerMinute", 10);
        var inquiriesPer15Min = config.GetValue("RateLimits:InquiriesPer15Minutes", 5);
        // A couple scrolling quickly through a 1,500-photo wedding gallery requests one thumbnail per photo.
        var apiPerMinute = config.GetValue("RateLimits:ApiPerMinute", 1500);

        o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        o.OnRejected = async (ctx, ct) =>
        {
            if (ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retry))
                ctx.HttpContext.Response.Headers.RetryAfter = ((int)retry.TotalSeconds).ToString();
            await Results.Problem(statusCode: 429, title: "Too many requests.", detail: "Please wait a moment and try again.")
                .ExecuteAsync(ctx.HttpContext);
        };

        // Brute-force protection per IP (per-account lockout is enforced separately in the database).
        o.AddPolicy(Login, ctx => RateLimitPartition.GetFixedWindowLimiter(ClientIp(ctx),
            _ => new FixedWindowRateLimiterOptions { PermitLimit = loginPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));

        o.AddPolicy(Inquiry, ctx => RateLimitPartition.GetFixedWindowLimiter(ClientIp(ctx),
            _ => new FixedWindowRateLimiterOptions { PermitLimit = inquiriesPer15Min, Window = TimeSpan.FromMinutes(15), QueueLimit = 0 }));

        // Generous global ceiling for the API; a gallery page legitimately fires hundreds of thumbnail requests.
        o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
            ctx.Request.Path.StartsWithSegments("/api")
                ? RateLimitPartition.GetTokenBucketLimiter(ClientIp(ctx), _ => new TokenBucketRateLimiterOptions
                {
                    TokenLimit = apiPerMinute,
                    TokensPerPeriod = apiPerMinute,
                    ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                })
                : RateLimitPartition.GetNoLimiter("static"));
    });

    private static string ClientIp(HttpContext ctx) => ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
