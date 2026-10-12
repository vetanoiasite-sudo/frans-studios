using System.Net;
using System.Net.Http.Json;
using FransStudios.Api.Authentication;
using FransStudios.Api.Data;
using FransStudios.Api.Entities;
using FransStudios.Api.GoogleDrive;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FransStudios.Api.Tests.Infrastructure;

/// <summary>
/// Boots the real API against its own throw-away LocalDB database, with Google's endpoints faked at the
/// HTTP layer. Seeds: one admin, client Alice (published + draft + downloads-disabled albums) and client Bob.
/// </summary>
public sealed class TestApp : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string AdminPassword = "Admin-Password-123";
    public const string AlicePassword = "Alice-Password-123";
    public const string BobPassword = "Bob-Password-123";

    private readonly string _dbName = $"FransStudios_Test_{Guid.NewGuid():N}";
    private readonly string _dataDir = Path.Combine(Path.GetTempPath(), "fs-tests", Guid.NewGuid().ToString("N"));

    public FakeDrive Drive { get; } = new();

    public int AliceClientId { get; private set; }
    public int BobClientId { get; private set; }
    public Album AlicePublished { get; private set; } = null!;
    public Album AliceDraft { get; private set; } = null!;
    public Album AliceNoDownloads { get; private set; } = null!;
    public Album BobPublished { get; private set; } = null!;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("AllowedHosts", "*");
        builder.UseSetting("ConnectionStrings:Default",
            $"Server=(localdb)\\MSSQLLocalDB;Database={_dbName};Trusted_Connection=True;TrustServerCertificate=True");
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("Site:PublicBaseUrl", "https://localhost");
        builder.UseSetting("Storage:PublicMediaPath", Path.Combine(_dataDir, "public"));
        builder.UseSetting("Storage:PrivateCachePath", Path.Combine(_dataDir, "private"));
        builder.UseSetting("Storage:DataProtectionKeysPath", Path.Combine(_dataDir, "keys"));
        builder.UseSetting("GoogleDrive:ClientId", "test-client");
        builder.UseSetting("GoogleDrive:ClientSecret", "test-secret");
        builder.UseSetting("GoogleDrive:UploadChunkMb", "1");
        builder.UseSetting("RateLimits:LoginPerMinute", "1000");
        builder.UseSetting("RateLimits:ApiPerMinute", "100000");

        builder.ConfigureTestServices(services =>
        {
            services.AddHttpClient(DriveHttp.ApiClient).ConfigurePrimaryHttpMessageHandler(() => new FakeGoogleHandler(Drive));
            services.AddHttpClient(DriveHttp.MediaClient).ConfigurePrimaryHttpMessageHandler(() => new FakeGoogleHandler(Drive));
        });
    }

    public async Task InitializeAsync()
    {
        BuildFakeDrive();
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var passwords = scope.ServiceProvider.GetRequiredService<PasswordService>();
        var protector = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("FransStudios.GoogleDrive.RefreshToken.v1");

        var admin = NewUser("admin", UserRole.Admin, AdminPassword, passwords);
        var alice = new Client { FullName = "Alice Example" };
        var bob = new Client { FullName = "Bob Example" };
        var aliceUser = NewUser("alice", UserRole.Client, AlicePassword, passwords, alice);
        var bobUser = NewUser("bob", UserRole.Client, BobPassword, passwords, bob);
        db.Users.AddRange(admin, aliceUser, bobUser);
        await db.SaveChangesAsync();

        db.GoogleDriveConnections.Add(new GoogleDriveConnection
        {
            AccountEmail = "studio@example.com",
            EncryptedRefreshToken = protector.Protect("fake-refresh-token"),
            Scopes = "https://www.googleapis.com/auth/drive",
            ConnectedByUserId = admin.Id,
            ConnectedAt = DateTime.UtcNow,
        });

        AlicePublished = new Album { Client = alice, Title = "Alice Wedding", Status = AlbumStatus.Published, DriveFolderId = "folderAlice0001" };
        AliceDraft = new Album { Client = alice, Title = "Alice Draft", Status = AlbumStatus.Draft, DriveFolderId = "folderAliceDraft" };
        AliceNoDownloads = new Album { Client = alice, Title = "Alice View Only", Status = AlbumStatus.Published, DownloadsEnabled = false, DriveFolderId = "folderAliceView1" };
        BobPublished = new Album { Client = bob, Title = "Bob Wedding", Status = AlbumStatus.Published, DriveFolderId = "folderBob000001" };
        db.Albums.AddRange(AlicePublished, AliceDraft, AliceNoDownloads, BobPublished);
        await db.SaveChangesAsync();
        AliceClientId = alice.Id;
        BobClientId = bob.Id;

        var sync = scope.ServiceProvider.GetRequiredService<AlbumSyncService>();
        foreach (var a in new[] { AlicePublished, AliceDraft, AliceNoDownloads, BobPublished })
        {
            var result = await sync.SyncAsync(a.Id, CancellationToken.None);
            if (!result.Success) throw new InvalidOperationException($"Seed sync failed: {result.Error}");
        }
    }

    private void BuildFakeDrive()
    {
        Drive.Folder("folderAlice0001", "Alice Wedding");
        Drive.Folder("folderAlicePhot", "Photos", "folderAlice0001");
        Drive.Folder("folderAliceVids", "Videos", "folderAlice0001");
        Drive.File("fileAlicePhoto10", "IMG_10.jpg", "image/jpeg", "folderAlicePhot");
        Drive.File("fileAlicePhoto02", "IMG_2.jpg", "image/jpeg", "folderAlicePhot");
        Drive.File("fileAliceVideo01", "Film.mp4", "video/mp4", "folderAliceVids", size: 5000);
        Drive.File("fileAliceSvg0001", "logo.svg", "image/svg+xml", "folderAlice0001");
        Drive.File("fileAliceShortcut", "Shortcut to elsewhere", "application/vnd.google-apps.shortcut", "folderAlice0001");
        Drive.File("fileAliceGoogleDoc", "Notes", "application/vnd.google-apps.document", "folderAlice0001");

        Drive.Folder("folderAliceDraft", "Alice Draft");
        Drive.File("fileAliceDraft01", "draft.jpg", "image/jpeg", "folderAliceDraft");

        Drive.Folder("folderAliceView1", "Alice View Only");
        Drive.File("fileAliceView001", "view.jpg", "image/jpeg", "folderAliceView1");

        Drive.Folder("folderBob000001", "Bob Wedding");
        Drive.File("fileBobPhoto0001", "bob.jpg", "image/jpeg", "folderBob000001");
        Drive.File("fileBobVideo0001", "bob.mp4", "video/mp4", "folderBob000001");
    }

    private static User NewUser(string username, UserRole role, string password, PasswordService passwords, Client? client = null)
    {
        var user = new User { Username = username, Role = role, Client = client };
        user.PasswordHash = passwords.Hash(user, password);
        return user;
    }

    /// <summary>Ids of the indexed files of an album, in album order.</summary>
    public async Task<List<AlbumFile>> FilesOfAsync(Album album)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.AlbumFiles.AsNoTracking().Where(f => f.AlbumId == album.Id && !f.IsRemoved).OrderBy(f => f.SortOrder).ToListAsync();
    }

    public ApiSession NewSession() => new(this);

    public async Task<ApiSession> LoginAsync(string username, string password)
    {
        var session = NewSession();
        var resp = await session.LoginAsync(username, password);
        if (resp.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException($"Login as {username} failed: {(int)resp.StatusCode} {await resp.Content.ReadAsStringAsync()}");
        return session;
    }

    public new async Task DisposeAsync()
    {
        try
        {
            await using var scope = Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureDeletedAsync();
        }
        finally
        {
            await base.DisposeAsync();
            try { Directory.Delete(_dataDir, recursive: true); } catch (IOException) { }
        }
    }
}

/// <summary>A browser-like client: keeps cookies and echoes the XSRF token on unsafe requests.</summary>
public sealed class ApiSession
{
    private readonly CookieContainer _cookies = new();
    private static readonly Uri Base = new("https://localhost");
    public HttpClient Http { get; }

    public ApiSession(TestApp app)
    {
        Http = app.CreateDefaultClient(Base, new CookieContainerHandler(_cookies));
    }

    public string? Cookie(string name) => _cookies.GetCookies(Base)[name]?.Value;

    public async Task EnsureCsrfAsync()
    {
        if (Cookie("XSRF-TOKEN") is null) (await Http.GetAsync("/api/auth/csrf")).EnsureSuccessStatusCode();
    }

    public async Task<HttpResponseMessage> LoginAsync(string username, string password)
    {
        await EnsureCsrfAsync();
        return await SendAsync(HttpMethod.Post, "/api/auth/login", new { username, password });
    }

    public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, object? body = null, bool withCsrf = true)
    {
        var req = new HttpRequestMessage(method, url);
        if (body is not null) req.Content = JsonContent.Create(body);
        if (withCsrf && Cookie("XSRF-TOKEN") is { } token) req.Headers.Add("X-XSRF-TOKEN", token);
        return await Http.SendAsync(req);
    }

    public Task<HttpResponseMessage> GetAsync(string url) => Http.GetAsync(url);
}
