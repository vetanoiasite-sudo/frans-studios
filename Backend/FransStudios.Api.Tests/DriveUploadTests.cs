using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FransStudios.Api.Data;
using FransStudios.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FransStudios.Api.Tests;

/// <summary>
/// Uploading client deliveries from the admin into the studio's Google Drive: the folder new albums go in,
/// one Drive folder per album, and resumable uploads — straight from the browser to Google (Direct) or
/// through the server (Proxy chunks) — that end up in the album like files the studio adds by hand.
/// </summary>
public sealed class DriveUploadTests(TestApp app) : IClassFixture<TestApp>
{
    private const int Chunk = 1024 * 1024; // TestApp sets GoogleDrive:UploadChunkMb = 1

    [Fact]
    public async Task New_album_can_create_its_own_drive_folder_with_photos_and_videos()
    {
        var admin = await app.LoginAsync("admin", TestApp.AdminPassword);
        Assert.True((await Json(await admin.GetAsync("/api/admin/google-drive/status"))).GetProperty("canUpload").GetBoolean());

        var first = await CreateAlbumAsync(admin, "Sara & Ahmed — Wedding");
        var second = await CreateAlbumAsync(admin, "Sara & Ahmed — Wedding");

        var folder1 = app.Drive.Items[first.GetProperty("driveFolderId").GetString()!];
        var folder2 = app.Drive.Items[second.GetProperty("driveFolderId").GetString()!];
        var root = app.Drive.Items[folder1.ParentId!];
        Assert.Equal(("Vetanoia Albums", "root"), (root.Name, root.ParentId)); // created by the website, in My Drive
        Assert.Equal(root.Id, folder2.ParentId);
        Assert.Single(app.Drive.Items.Values, i => i.Name == "Vetanoia Albums"); // created once, then reused
        Assert.Equal("Sara & Ahmed — Wedding", folder1.Name);
        Assert.Equal("Sara & Ahmed — Wedding (2)", folder2.Name); // never merges two albums into one folder
        Assert.Equal(["Photos", "Videos"], app.Drive.Items.Values.Where(i => i.ParentId == folder1.Id).Select(i => i.Name).Order());
        Assert.Equal("My Drive / Vetanoia Albums / Sara & Ahmed — Wedding", first.GetProperty("driveFolderName").GetString());
    }

    [Fact]
    public async Task Albums_folder_is_found_again_when_the_remembered_one_is_gone()
    {
        // For example after switching Google accounts: the remembered folder is in the other account's Drive.
        var admin = await app.LoginAsync("admin", TestApp.AdminPassword);
        var rootId = app.Drive.Items[(await CreateAlbumAsync(admin, "Before Switch")).GetProperty("driveFolderId").GetString()!].ParentId;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.SiteSettings.Where(s => s.Key == "drive.uploadRoot.id")
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.Value, "folderInAnotherAccount"));
        }

        var album = await CreateAlbumAsync(admin, "After Switch");
        Assert.Equal(rootId, app.Drive.Items[album.GetProperty("driveFolderId").GetString()!].ParentId);
    }

    [Fact]
    public async Task Browser_uploads_straight_to_google_and_the_file_joins_the_album()
    {
        var admin = await app.LoginAsync("admin", TestApp.AdminPassword);
        var album = await CreateAlbumAsync(admin, "Direct Upload Wedding");
        var albumId = album.GetProperty("id").GetInt32();
        var bytes = Bytes(Chunk * 2 + 500_000);

        var session = await StartAsync(admin, albumId, "IMG_0001.jpg", bytes.Length, "Photos");
        Assert.Equal("direct", session.GetProperty("mode").GetString());
        Assert.Equal(Chunk, session.GetProperty("chunkSize").GetInt32());
        var sessionUrl = session.GetProperty("sessionUrl").GetString()!;
        var uploadId = session.GetProperty("uploadId").GetString()!;
        Assert.StartsWith("https://www.googleapis.com/upload/drive/v3/files?", sessionUrl);
        // The session was opened for the site's origin, so Google accepts the browser's chunks.
        Assert.Equal("https://localhost", app.Drive.Uploads.Values.Single(u => u.Name == "IMG_0001.jpg").Origin);

        // The "browser": no Google token, just the session URL.
        using var browser = new HttpClient(new FakeGoogleHandler(app.Drive));
        var first = await PutChunkAsync(browser, sessionUrl, bytes, 0, Chunk);
        Assert.Equal(308, (int)first.StatusCode);

        // After an interruption the admin page asks the server how far Google got.
        var status = await Json(await admin.SendAsync(HttpMethod.Post, $"/api/admin/albums/{albumId}/uploads/status", new { uploadId }));
        Assert.Equal(Chunk, status.GetProperty("received").GetInt64());
        Assert.False(status.GetProperty("complete").GetBoolean());

        // Completing early is refused.
        var early = await admin.SendAsync(HttpMethod.Post, $"/api/admin/albums/{albumId}/uploads/complete", new { uploadId });
        Assert.Equal(HttpStatusCode.Conflict, early.StatusCode);

        Assert.Equal(308, (int)(await PutChunkAsync(browser, sessionUrl, bytes, Chunk, Chunk)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PutChunkAsync(browser, sessionUrl, bytes, Chunk * 2, bytes.Length - Chunk * 2)).StatusCode);

        var complete = await admin.SendAsync(HttpMethod.Post, $"/api/admin/albums/{albumId}/uploads/complete", new { uploadId });
        Assert.Equal(HttpStatusCode.OK, complete.StatusCode);
        var driveFileId = (await Json(complete)).GetProperty("driveFileId").GetString()!;
        Assert.Equal(bytes, app.Drive.Items[driveFileId].Bytes);

        // The album sync (queued by "complete"; run here directly) indexes it under Photos.
        (await admin.SendAsync(HttpMethod.Post, $"/api/admin/albums/{albumId}/sync")).EnsureSuccessStatusCode();
        var files = await Json(await admin.GetAsync($"/api/admin/albums/{albumId}/files"));
        var file = Assert.Single(files.GetProperty("items").EnumerateArray());
        Assert.Equal("IMG_0001.jpg", file.GetProperty("name").GetString());
        Assert.Equal("Photos", file.GetProperty("section").GetString());
        Assert.Equal(bytes.Length, file.GetProperty("sizeBytes").GetInt64());
    }

    [Fact]
    public async Task Chunks_can_go_through_the_server_and_resume_after_a_repeat()
    {
        var admin = await app.LoginAsync("admin", TestApp.AdminPassword);
        var album = await CreateAlbumAsync(admin, "Proxy Upload Wedding");
        var albumId = album.GetProperty("id").GetInt32();
        var folderId = album.GetProperty("driveFolderId").GetString()!;
        var bytes = Bytes(Chunk + 1234);

        // A new section creates its sub-folder on first use.
        var session = await StartAsync(admin, albumId, "Highlights.mp4", bytes.Length, "Highlights", "video/mp4");
        var uploadId = session.GetProperty("uploadId").GetString()!;
        Assert.Contains(app.Drive.Items.Values, i => i.ParentId == folderId && i.Name == "Highlights");

        var r1 = await ProxyChunkAsync(admin, albumId, uploadId, bytes, 0, Chunk);
        Assert.Equal(Chunk, (await Json(r1)).GetProperty("received").GetInt64());
        // The same chunk sent twice (e.g. a retry after a dropped response) doesn't corrupt the file.
        var again = await ProxyChunkAsync(admin, albumId, uploadId, bytes, 0, Chunk);
        Assert.Equal(Chunk, (await Json(again)).GetProperty("received").GetInt64());
        var last = await Json(await ProxyChunkAsync(admin, albumId, uploadId, bytes, Chunk, bytes.Length - Chunk));
        Assert.True(last.GetProperty("complete").GetBoolean());

        var complete = await admin.SendAsync(HttpMethod.Post, $"/api/admin/albums/{albumId}/uploads/complete", new { uploadId });
        Assert.Equal(HttpStatusCode.OK, complete.StatusCode);
        Assert.Equal(bytes, app.Drive.Items[(await Json(complete)).GetProperty("driveFileId").GetString()!].Bytes);
    }

    [Fact]
    public async Task Upload_requests_are_validated()
    {
        var admin = await app.LoginAsync("admin", TestApp.AdminPassword);
        var a = (await CreateAlbumAsync(admin, "Validation A")).GetProperty("id").GetInt32();
        var b = (await CreateAlbumAsync(admin, "Validation B")).GetProperty("id").GetInt32();
        var session = await StartAsync(admin, a, "x.jpg", Chunk * 3, "Photos");
        var uploadId = session.GetProperty("uploadId").GetString()!;

        // A ticket only works for the album it was issued for, and can't be forged.
        Assert.Equal(HttpStatusCode.NotFound, (await admin.SendAsync(HttpMethod.Post, $"/api/admin/albums/{b}/uploads/status", new { uploadId })).StatusCode);
        Assert.Equal(HttpStatusCode.Gone, (await admin.SendAsync(HttpMethod.Post, $"/api/admin/albums/{a}/uploads/status", new { uploadId = uploadId + "x" })).StatusCode);

        // Chunks must be the agreed size except the last one.
        Assert.Equal(HttpStatusCode.BadRequest, (await ProxyChunkAsync(admin, a, uploadId, Bytes(Chunk * 3), 0, 1000)).StatusCode);

        // Section names are single folder names.
        var slash = await admin.SendAsync(HttpMethod.Post, $"/api/admin/albums/{a}/uploads",
            new { fileName = "x.jpg", size = 10, mimeType = "image/jpeg", section = "../Other" });
        Assert.Equal(HttpStatusCode.BadRequest, slash.StatusCode);

        // An album without a Drive folder can't receive uploads.
        var noFolder = await admin.SendAsync(HttpMethod.Post, "/api/admin/albums",
            new { clientId = app.AliceClientId, title = "No folder", status = "draft" });
        var noFolderId = (await Json(noFolder)).GetProperty("id").GetInt32();
        var refused = await admin.SendAsync(HttpMethod.Post, $"/api/admin/albums/{noFolderId}/uploads",
            new { fileName = "x.jpg", size = 10, mimeType = "image/jpeg", section = "Photos" });
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
    }

    [Fact]
    public async Task Clients_cannot_upload_and_read_only_connections_must_reconnect()
    {
        var admin = await app.LoginAsync("admin", TestApp.AdminPassword);
        var albumId = (await CreateAlbumAsync(admin, "Permissions Wedding")).GetProperty("id").GetInt32();

        var alice = await app.LoginAsync("alice", TestApp.AlicePassword);
        var asClient = await alice.SendAsync(HttpMethod.Post, $"/api/admin/albums/{albumId}/uploads",
            new { fileName = "x.jpg", size = 10, mimeType = "image/jpeg", section = "Photos" });
        Assert.Equal(HttpStatusCode.Forbidden, asClient.StatusCode);

        await SetScopesAsync("https://www.googleapis.com/auth/drive.readonly");
        try
        {
            var readOnly = await admin.SendAsync(HttpMethod.Post, $"/api/admin/albums/{albumId}/uploads",
                new { fileName = "x.jpg", size = 10, mimeType = "image/jpeg", section = "Photos" });
            Assert.Equal(HttpStatusCode.Conflict, readOnly.StatusCode);
            Assert.Contains("Reconnect", (await Json(readOnly)).GetProperty("title").GetString());
            Assert.False((await Json(await admin.GetAsync("/api/admin/google-drive/status"))).GetProperty("canUpload").GetBoolean());
        }
        finally { await SetScopesAsync("https://www.googleapis.com/auth/drive"); }
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private async Task<JsonElement> CreateAlbumAsync(ApiSession admin, string title)
    {
        var resp = await admin.SendAsync(HttpMethod.Post, "/api/admin/albums",
            new { clientId = app.AliceClientId, title, status = "draft", createDriveFolder = true });
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        return await Json(resp);
    }

    private static async Task<JsonElement> StartAsync(ApiSession admin, int albumId, string name, long size, string section, string mime = "image/jpeg")
    {
        var resp = await admin.SendAsync(HttpMethod.Post, $"/api/admin/albums/{albumId}/uploads",
            new { fileName = name, size, mimeType = mime, section });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        return await Json(resp);
    }

    private static Task<HttpResponseMessage> PutChunkAsync(HttpClient browser, string sessionUrl, byte[] bytes, int offset, int length)
    {
        var content = new ByteArrayContent(bytes, offset, length);
        content.Headers.TryAddWithoutValidation("Content-Range", $"bytes {offset}-{offset + length - 1}/{bytes.Length}");
        return browser.PutAsync(sessionUrl, content);
    }

    private static Task<HttpResponseMessage> ProxyChunkAsync(ApiSession admin, int albumId, string uploadId, byte[] bytes, int offset, int length)
    {
        var req = new HttpRequestMessage(HttpMethod.Put,
            $"/api/admin/albums/{albumId}/uploads/chunk?uploadId={Uri.EscapeDataString(uploadId)}&offset={offset}")
        {
            Content = new ByteArrayContent(bytes, offset, length),
        };
        req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        req.Headers.Add("X-XSRF-TOKEN", admin.Cookie("XSRF-TOKEN"));
        return admin.Http.SendAsync(req);
    }

    private async Task SetScopesAsync(string scopes)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.GoogleDriveConnections.Where(c => c.RevokedAt == null).ExecuteUpdateAsync(u => u.SetProperty(c => c.Scopes, scopes));
    }

    private static byte[] Bytes(int length)
    {
        var bytes = new byte[length];
        new Random(length).NextBytes(bytes);
        return bytes;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage resp) =>
        (await resp.Content.ReadFromJsonAsync<JsonElement>());
}
