using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FransStudios.Api.Tests.Infrastructure;

namespace FransStudios.Api.Tests;

/// <summary>
/// The admin Drive manager: browsing, searching and storage of the whole studio Drive; new folders, rename,
/// move, trash (never a permanent delete) and uploads into any folder; albums follow the changes.
/// </summary>
public sealed class DriveManagerTests(TestApp app) : IClassFixture<TestApp>
{
    [Fact]
    public async Task Lists_folders_first_searches_and_reports_storage()
    {
        var admin = await app.LoginAsync("admin", TestApp.AdminPassword);

        var list = await Json(await admin.GetAsync("/api/admin/drive/items?parentId=folderAlice0001"));
        var items = list.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(["Photos", "Videos"], items.TakeWhile(i => i.GetProperty("isFolder").GetBoolean()).Select(i => i.GetProperty("name").GetString()));
        var doc = items.Single(i => i.GetProperty("name").GetString() == "Notes");
        Assert.True(doc.GetProperty("isGoogleDoc").GetBoolean());
        Assert.True(items.First().GetProperty("canRename").GetBoolean());

        var found = await Json(await admin.GetAsync("/api/admin/drive/items?q=img_"));
        Assert.Equal(["IMG_10.jpg", "IMG_2.jpg"], found.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("name").GetString()).Order());

        var storage = await Json(await admin.GetAsync("/api/admin/drive/storage"));
        Assert.Equal(16106127360, storage.GetProperty("limit").GetInt64());
        Assert.Equal(1073741824, storage.GetProperty("usageInTrash").GetInt64());

        var trail = await Json(await admin.GetAsync("/api/admin/drive/items/folderAlicePhot/trail"));
        Assert.Equal(["Alice Wedding", "Photos"], trail.EnumerateArray().Select(c => c.GetProperty("name").GetString()));
    }

    [Fact]
    public async Task Folder_rename_move_and_trash_and_the_album_follows()
    {
        var admin = await app.LoginAsync("admin", TestApp.AdminPassword);
        app.Drive.File("fileBobExtra001", "extra.jpg", "image/jpeg", "folderBob000001");
        (await admin.SendAsync(HttpMethod.Post, $"/api/admin/albums/{app.BobPublished.Id}/sync")).EnsureSuccessStatusCode();
        Assert.Equal(3, (await app.FilesOfAsync(app.BobPublished)).Count);

        var created = await admin.SendAsync(HttpMethod.Post, "/api/admin/drive/folders", new { name = "Archive", parentId = "root" });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var archiveId = (await Json(created)).GetProperty("id").GetString()!;
        Assert.Equal("root", app.Drive.Items[archiveId].ParentId);

        var renamed = await admin.SendAsync(HttpMethod.Patch, $"/api/admin/drive/items/{archiveId}", new { name = "Archive 2025" });
        Assert.Equal("Archive 2025", (await Json(renamed)).GetProperty("name").GetString());

        // Moving a file out of Bob's album folder removes it from his gallery.
        var moved = await Json(await admin.SendAsync(HttpMethod.Post, "/api/admin/drive/move", new { ids = new[] { "fileBobExtra001" }, toFolderId = archiveId }));
        Assert.Equal(["fileBobExtra001"], moved.GetProperty("done").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal(archiveId, app.Drive.Items["fileBobExtra001"].ParentId);
        await WaitForAsync(async () => (await app.FilesOfAsync(app.BobPublished)).Count == 2);

        // Trash: gone from listings (restorable in Google Drive), and the album drops it too.
        var trashed = await Json(await admin.SendAsync(HttpMethod.Post, "/api/admin/drive/trash", new { ids = new[] { "fileBobVideo0001", "no-such-file-xyz" } }));
        Assert.Equal(["fileBobVideo0001"], trashed.GetProperty("done").EnumerateArray().Select(x => x.GetString()));
        Assert.Single(trashed.GetProperty("failed").EnumerateArray());
        Assert.True(app.Drive.Trashed.ContainsKey("fileBobVideo0001"));
        Assert.True(app.Drive.Items.ContainsKey("fileBobVideo0001")); // trashed, not deleted
        var bobFolder = await Json(await admin.GetAsync("/api/admin/drive/items?parentId=folderBob000001"));
        Assert.DoesNotContain(bobFolder.GetProperty("items").EnumerateArray(), i => i.GetProperty("id").GetString() == "fileBobVideo0001");
        await WaitForAsync(async () => (await app.FilesOfAsync(app.BobPublished)).Count == 1);
    }

    [Fact]
    public async Task Uploads_go_into_any_folder_and_tickets_stay_in_their_lane()
    {
        var admin = await app.LoginAsync("admin", TestApp.AdminPassword);
        var bytes = new byte[300_000];
        new Random(7).NextBytes(bytes);

        var session = await Json(await admin.SendAsync(HttpMethod.Post, "/api/admin/drive/uploads",
            new { parentId = "folderAliceVids", fileName = "Teaser.mp4", size = bytes.Length, mimeType = "video/mp4" }));
        var uploadId = session.GetProperty("uploadId").GetString()!;

        // A Drive-manager ticket can't be used on an album's upload endpoints (and vice versa).
        var crossed = await admin.SendAsync(HttpMethod.Post, $"/api/admin/albums/{app.AlicePublished.Id}/uploads/status", new { uploadId });
        Assert.Equal(HttpStatusCode.NotFound, crossed.StatusCode);

        var req = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/drive/uploads/chunk?uploadId={Uri.EscapeDataString(uploadId)}&offset=0")
        {
            Content = new ByteArrayContent(bytes),
        };
        req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        req.Headers.Add("X-XSRF-TOKEN", admin.Cookie("XSRF-TOKEN"));
        Assert.True((await Json(await admin.Http.SendAsync(req))).GetProperty("complete").GetBoolean());

        var complete = await admin.SendAsync(HttpMethod.Post, "/api/admin/drive/uploads/complete", new { uploadId });
        Assert.Equal(HttpStatusCode.OK, complete.StatusCode);
        var fileId = (await Json(complete)).GetProperty("driveFileId").GetString()!;
        Assert.Equal("folderAliceVids", app.Drive.Items[fileId].ParentId);
        // It lands in Alice's album (Videos is one level under the album folder).
        await WaitForAsync(async () => (await app.FilesOfAsync(app.AlicePublished)).Any(f => f.Name == "Teaser.mp4"));

        // Download streams the bytes; Google Docs are opened in Drive instead.
        var download = await admin.GetAsync($"/api/admin/drive/items/{fileId}/content?download=true");
        Assert.Equal(bytes, await download.Content.ReadAsByteArrayAsync());
        Assert.Equal("attachment", download.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/admin/drive/items/fileAliceGoogleDoc/content")).StatusCode);
    }

    [Fact]
    public async Task Album_folders_are_marked_and_a_folder_belongs_to_one_album_only()
    {
        var admin = await app.LoginAsync("admin", TestApp.AdminPassword);

        var item = await Json(await admin.GetAsync("/api/admin/drive/items/folderBob000001"));
        var album = item.GetProperty("album");
        Assert.Equal(app.BobPublished.Id, album.GetProperty("id").GetInt32());
        Assert.Equal("Bob Example", album.GetProperty("clientName").GetString());
        Assert.Equal("published", album.GetProperty("status").GetString());
        var plain = await Json(await admin.GetAsync("/api/admin/drive/items/folderAlicePhot"));
        Assert.Equal(JsonValueKind.Null, plain.GetProperty("album").ValueKind);

        // Linking Bob's folder to Alice is refused; a free folder links fine.
        var taken = await admin.SendAsync(HttpMethod.Post, "/api/admin/albums",
            new { clientId = app.AliceClientId, title = "Not hers", status = "draft", driveFolder = "folderBob000001" });
        Assert.Equal(HttpStatusCode.BadRequest, taken.StatusCode);
        Assert.Contains("Bob Wedding", await taken.Content.ReadAsStringAsync());

        app.Drive.Folder("folderFreeWedding", "Free Wedding", "root");
        var linked = await admin.SendAsync(HttpMethod.Post, "/api/admin/albums",
            new { clientId = app.AliceClientId, title = "Free Wedding", status = "draft", driveFolder = "folderFreeWedding" });
        Assert.Equal(HttpStatusCode.Created, linked.StatusCode);
        var root = await Json(await admin.GetAsync("/api/admin/drive/items"));
        var row = root.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("id").GetString() == "folderFreeWedding");
        Assert.Equal("Alice Example", row.GetProperty("album").GetProperty("clientName").GetString());
    }

    [Fact]
    public async Task Album_cover_can_be_set_from_the_drive_page_and_films_only_albums_get_a_video_cover()
    {
        var admin = await app.LoginAsync("admin", TestApp.AdminPassword);

        // Inside an album's Photos folder the listing says which album it belongs to.
        var photos = await Json(await admin.GetAsync("/api/admin/drive/items?parentId=folderAlicePhot"));
        Assert.Equal(app.AlicePublished.Id, photos.GetProperty("folderAlbum").GetProperty("id").GetInt32());

        // A photo uploaded a moment ago (not indexed yet) can become the cover: the album syncs first.
        app.Drive.File("fileAliceNew0001", "Cover_99.jpg", "image/jpeg", "folderAlicePhot");
        var set = await admin.SendAsync(HttpMethod.Put, "/api/admin/drive/items/fileAliceNew0001/album-cover");
        Assert.Equal(HttpStatusCode.OK, set.StatusCode);
        Assert.Equal(app.AlicePublished.Id, (await Json(set)).GetProperty("albumId").GetInt32());
        var album = await Json(await admin.GetAsync($"/api/admin/albums/{app.AlicePublished.Id}"));
        var cover = (await app.FilesOfAsync(app.AlicePublished)).Single(f => f.DriveFileId == "fileAliceNew0001");
        Assert.Equal(cover.Id, album.GetProperty("coverFileId").GetGuid());

        // A video can be the cover too; a file outside any album can't.
        Assert.Equal(HttpStatusCode.OK, (await admin.SendAsync(HttpMethod.Put, "/api/admin/drive/items/fileAliceVideo01/album-cover")).StatusCode);
        app.Drive.File("fileLoose0000001", "loose.jpg", "image/jpeg", "root");
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.SendAsync(HttpMethod.Put, "/api/admin/drive/items/fileLoose0000001/album-cover")).StatusCode);

        // A films-only album gets its first video's preview as the default cover.
        app.Drive.Folder("folderFilmsOnly1", "Films Only", "root");
        app.Drive.File("fileFilmOnly0001", "Highlights.mp4", "video/mp4", "folderFilmsOnly1");
        var films = await Json(await admin.SendAsync(HttpMethod.Post, "/api/admin/albums",
            new { clientId = app.AliceClientId, title = "Films Only", status = "draft", driveFolder = "folderFilmsOnly1" }));
        Assert.Equal(JsonValueKind.String, films.GetProperty("coverFileId").ValueKind);
    }

    [Fact]
    public async Task Clients_cannot_reach_the_drive_manager()
    {
        var alice = await app.LoginAsync("alice", TestApp.AlicePassword);
        Assert.Equal(HttpStatusCode.Forbidden, (await alice.GetAsync("/api/admin/drive/items")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await alice.GetAsync("/api/admin/drive/items/fileAlicePhoto10/content")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await alice.SendAsync(HttpMethod.Post, "/api/admin/drive/trash", new { ids = new[] { "fileAlicePhoto10" } })).StatusCode);
        Assert.False(app.Drive.Trashed.ContainsKey("fileAlicePhoto10"));
    }

    private static async Task WaitForAsync(Func<Task<bool>> condition)
    {
        for (var i = 0; i < 50; i++)
        {
            if (await condition()) return;
            await Task.Delay(100);
        }
        Assert.Fail("Timed out waiting for the album to re-sync.");
    }

    private static async Task<JsonElement> Json(HttpResponseMessage resp) => await resp.Content.ReadFromJsonAsync<JsonElement>();
}
