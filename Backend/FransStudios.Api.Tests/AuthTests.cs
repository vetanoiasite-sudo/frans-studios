using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FransStudios.Api.Tests.Infrastructure;

namespace FransStudios.Api.Tests;

public sealed class AuthTests(TestApp app) : IClassFixture<TestApp>
{
    [Fact]
    public async Task Login_sets_hardened_cookie_and_me_returns_role()
    {
        var s = app.NewSession();
        await s.EnsureCsrfAsync();
        var resp = await s.SendAsync(HttpMethod.Post, "/api/auth/login", new { username = "ALICE ", password = TestApp.AlicePassword });

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var setCookie = string.Join("\n", resp.Headers.GetValues("Set-Cookie"));
        Assert.Contains("__Host-fs_auth=", setCookie);
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", await resp.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        var me = await s.Http.GetFromJsonAsync<JsonElement>("/api/auth/me");
        Assert.Equal("client", me.GetProperty("role").GetString());
        Assert.Equal("Alice Example", me.GetProperty("displayName").GetString());
    }

    [Fact]
    public async Task Wrong_password_and_unknown_user_look_identical()
    {
        var s = app.NewSession();
        var wrong = await s.LoginAsync("bob", "not-the-password");
        var unknown = await s.LoginAsync("nobody-here", "not-the-password");
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        var a = await wrong.Content.ReadFromJsonAsync<JsonElement>();
        var b = await unknown.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(a.GetProperty("title").GetString(), b.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Unsafe_requests_without_csrf_token_are_rejected()
    {
        var s = app.NewSession();
        await s.EnsureCsrfAsync();
        var resp = await s.SendAsync(HttpMethod.Post, "/api/auth/login",
            new { username = "alice", password = TestApp.AlicePassword }, withCsrf: false);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);

        var admin = await app.LoginAsync("admin", TestApp.AdminPassword);
        var create = await admin.SendAsync(HttpMethod.Post, "/api/admin/clients",
            new { fullName = "No Csrf", username = "nocsrf" }, withCsrf: false);
        Assert.Equal(HttpStatusCode.BadRequest, create.StatusCode);
    }

    [Fact]
    public async Task Account_locks_after_repeated_failures()
    {
        var admin = await app.LoginAsync("admin", TestApp.AdminPassword);
        var created = await (await admin.SendAsync(HttpMethod.Post, "/api/admin/clients",
            new { fullName = "Lock Test", username = "locktest" })).Content.ReadFromJsonAsync<JsonElement>();
        var password = created.GetProperty("password").GetString()!;

        var s = app.NewSession();
        for (var i = 0; i < 5; i++) Assert.Equal(HttpStatusCode.Unauthorized, (await s.LoginAsync("locktest", "wrong-password")).StatusCode);

        var locked = await s.LoginAsync("locktest", password);
        Assert.Equal(HttpStatusCode.Unauthorized, locked.StatusCode);
        Assert.Contains("Too many failed attempts", await locked.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Created_client_can_log_in_with_generated_password_once_shown()
    {
        var admin = await app.LoginAsync("admin", TestApp.AdminPassword);
        var resp = await admin.SendAsync(HttpMethod.Post, "/api/admin/clients",
            new { fullName = "Sara Ahmed", username = "Sara.Ahmed", email = "sara@example.com" });
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var password = body.GetProperty("password").GetString()!;
        Assert.Equal("sara.ahmed", body.GetProperty("username").GetString());
        Assert.Equal(16, password.Length);

        var sara = await app.LoginAsync("sara.ahmed", password);
        Assert.Equal(HttpStatusCode.OK, (await sara.GetAsync("/api/client/albums")).StatusCode);

        // Details never echo the password again.
        var id = body.GetProperty("clientId").GetInt32();
        var details = await admin.GetAsync($"/api/admin/clients/{id}");
        Assert.DoesNotContain(password, await details.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Duplicate_username_returns_409()
    {
        var admin = await app.LoginAsync("admin", TestApp.AdminPassword);
        var resp = await admin.SendAsync(HttpMethod.Post, "/api/admin/clients", new { fullName = "Another Alice", username = "alice" });
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
    }

    [Fact]
    public async Task Disabling_a_client_ends_their_session_immediately()
    {
        var admin = await app.LoginAsync("admin", TestApp.AdminPassword);
        var created = await (await admin.SendAsync(HttpMethod.Post, "/api/admin/clients",
            new { fullName = "Disable Me", username = "disableme" })).Content.ReadFromJsonAsync<JsonElement>();
        var client = await app.LoginAsync("disableme", created.GetProperty("password").GetString()!);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/client/albums")).StatusCode);

        var disable = await admin.SendAsync(HttpMethod.Post, $"/api/admin/clients/{created.GetProperty("clientId").GetInt32()}/disable");
        Assert.Equal(HttpStatusCode.NoContent, disable.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/client/albums")).StatusCode);
        var relogin = await app.NewSession().LoginAsync("disableme", created.GetProperty("password").GetString()!);
        Assert.Equal(HttpStatusCode.Unauthorized, relogin.StatusCode);
    }

    [Fact]
    public async Task Password_reset_invalidates_existing_sessions()
    {
        var admin = await app.LoginAsync("admin", TestApp.AdminPassword);
        var created = await (await admin.SendAsync(HttpMethod.Post, "/api/admin/clients",
            new { fullName = "Reset Me", username = "resetme" })).Content.ReadFromJsonAsync<JsonElement>();
        var oldPassword = created.GetProperty("password").GetString()!;
        var client = await app.LoginAsync("resetme", oldPassword);

        var reset = await admin.SendAsync(HttpMethod.Post, $"/api/admin/clients/{created.GetProperty("clientId").GetInt32()}/reset-password", new { });
        var newPassword = (await reset.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("password").GetString()!;

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/client/albums")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.NewSession().LoginAsync("resetme", oldPassword)).StatusCode);
        await app.LoginAsync("resetme", newPassword);
    }

    [Fact]
    public async Task Logout_revokes_the_session_server_side()
    {
        var s = await app.LoginAsync("bob", TestApp.BobPassword);
        var cookie = s.Cookie("__Host-fs_auth")!;
        Assert.Equal(HttpStatusCode.NoContent, (await s.SendAsync(HttpMethod.Post, "/api/auth/logout")).StatusCode);

        // Replaying the old cookie must not work.
        var replay = new HttpRequestMessage(HttpMethod.Get, "/api/client/albums");
        replay.Headers.Add("Cookie", $"__Host-fs_auth={cookie}");
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.CreateClient(new() { BaseAddress = new Uri("https://localhost") }).SendAsync(replay)).StatusCode);
    }

    [Fact]
    public async Task Api_errors_do_not_leak_internals()
    {
        var resp = await app.NewSession().GetAsync("/api/does-not-exist");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        Assert.Equal("application/problem+json", resp.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task Private_areas_are_noindex_and_security_headers_are_set()
    {
        var resp = await app.NewSession().GetAsync("/api/health");
        Assert.Equal("nosniff", resp.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", resp.Headers.GetValues("X-Frame-Options").Single());
        Assert.Contains("frame-ancestors 'none'", resp.Headers.GetValues("Content-Security-Policy").Single());

        var robots = await (await app.NewSession().GetAsync("/robots.txt")).Content.ReadAsStringAsync();
        Assert.Contains("Disallow: /admin", robots);
        Assert.Contains("Disallow: /client", robots);
    }
}
