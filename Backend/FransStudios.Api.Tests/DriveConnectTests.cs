using FransStudios.Api.Data;
using FransStudios.Api.GoogleDrive;
using FransStudios.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FransStudios.Api.Tests;

/// <summary>Finishing Google's consent flow stores the new connection (SQL Server runs with retry-on-failure).</summary>
public sealed class DriveConnectTests(TestApp app) : IClassFixture<TestApp>
{
    [Fact]
    public async Task Completing_consent_replaces_the_active_connection()
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tokens = scope.ServiceProvider.GetRequiredService<DriveTokenService>();
        var adminId = await db.Users.Where(u => u.Username == "admin").Select(u => u.Id).SingleAsync();
        var previousId = (await tokens.GetActiveConnectionAsync(CancellationToken.None))!.Id;

        var connection = await tokens.CompleteAuthorizationAsync("auth-code", "verifier", "https://localhost/callback", adminId, CancellationToken.None);

        Assert.Equal("studio@example.com", connection.AccountEmail);
        var active = await db.GoogleDriveConnections.AsNoTracking().Where(c => c.RevokedAt == null).ToListAsync();
        Assert.Equal(connection.Id, Assert.Single(active).Id);
        Assert.NotNull((await db.GoogleDriveConnections.AsNoTracking().SingleAsync(c => c.Id == previousId)).RevokedAt);
    }
}
