using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FransStudios.Api.Entities;
using FransStudios.Api.Tests.Infrastructure;

namespace FransStudios.Api.Tests;

/// <summary>
/// The core security guarantee: a client can only ever reach their own published albums and files.
/// Every probe against another client's (or an unpublished) resource must return 404.
/// </summary>
public sealed class ClientIsolationTests(TestApp app) : IClassFixture<TestApp>
{
    [Fact]
    public async Task Anonymous_requests_are_rejected()
    {
        var anon = app.NewSession();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/client/albums")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync($"/api/client/albums/{app.AlicePublished.PublicId}")).StatusCode);
        var file = (await app.FilesOfAsync(app.AlicePublished))[0];
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync($"/api/client/files/{file.Id}/content")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/admin/clients")).StatusCode);
    }

    [Fact]
    public async Task Client_cannot_use_admin_api()
    {
        var alice = await app.LoginAsync("alice", TestApp.AlicePassword);
        Assert.Equal(HttpStatusCode.Forbidden, (await alice.GetAsync("/api/admin/clients")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await alice.GetAsync("/api/admin/albums")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await alice.GetAsync("/api/admin/google-drive/folders")).StatusCode);
        var bobFile = (await app.FilesOfAsync(app.BobPublished))[0];
        Assert.Equal(HttpStatusCode.Forbidden, (await alice.GetAsync($"/api/admin/files/{bobFile.Id}/content")).StatusCode);
    }

    [Fact]
    public async Task Client_lists_only_own_published_albums()
    {
        var alice = await app.LoginAsync("alice", TestApp.AlicePassword);
        var albums = await alice.Http.GetFromJsonAsync<JsonElement>("/api/client/albums");
        var ids = albums.EnumerateArray().Select(a => a.GetProperty("id").GetGuid()).ToHashSet();

        Assert.Contains(app.AlicePublished.PublicId, ids);
        Assert.Contains(app.AliceNoDownloads.PublicId, ids);
        Assert.DoesNotContain(app.AliceDraft.PublicId, ids);
        Assert.DoesNotContain(app.BobPublished.PublicId, ids);
    }

    [Fact]
    public async Task Client_gets_404_for_another_clients_album_and_files()
    {
        var alice = await app.LoginAsync("alice", TestApp.AlicePassword);
        var bobAlbum = app.BobPublished.PublicId;
        var bobFile = (await app.FilesOfAsync(app.BobPublished))[0].Id;

        Assert.Equal(HttpStatusCode.NotFound, (await alice.GetAsync($"/api/client/albums/{bobAlbum}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.GetAsync($"/api/client/albums/{bobAlbum}/files")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.GetAsync($"/api/client/albums/{bobAlbum}/zip")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.GetAsync($"/api/client/files/{bobFile}/thumbnail")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.GetAsync($"/api/client/files/{bobFile}/content")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.GetAsync($"/api/client/files/{bobFile}/content?mode=download")).StatusCode);
    }

    [Fact]
    public async Task Client_gets_404_for_own_unpublished_album()
    {
        var alice = await app.LoginAsync("alice", TestApp.AlicePassword);
        var draftFile = (await app.FilesOfAsync(app.AliceDraft))[0].Id;
        Assert.Equal(HttpStatusCode.NotFound, (await alice.GetAsync($"/api/client/albums/{app.AliceDraft.PublicId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.GetAsync($"/api/client/files/{draftFile}/content")).StatusCode);
    }

    [Fact]
    public async Task Client_ids_in_the_request_are_ignored()
    {
        // There is no endpoint that accepts a client id, and extra query parameters must not change ownership.
        var alice = await app.LoginAsync("alice", TestApp.AlicePassword);
        var resp = await alice.GetAsync($"/api/client/albums/{app.BobPublished.PublicId}?clientId={app.BobClientId}");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Sync_indexes_subfolders_in_order_and_skips_google_native_files()
    {
        var files = await app.FilesOfAsync(app.AlicePublished);
        var names = files.Select(f => $"{f.Section}/{f.Name}").ToList();

        Assert.Equal(["/logo.svg", "Photos/IMG_2.jpg", "Photos/IMG_10.jpg", "Videos/Film.mp4"], names);
        Assert.DoesNotContain(files, f => f.MimeType.StartsWith("application/vnd.google-apps"));
        Assert.Equal(MediaKind.Video, files.Single(f => f.Name == "Film.mp4").Kind);
    }

    [Fact]
    public async Task Range_requests_are_forwarded_and_return_206()
    {
        var alice = await app.LoginAsync("alice", TestApp.AlicePassword);
        var video = (await app.FilesOfAsync(app.AlicePublished)).Single(f => f.Name == "Film.mp4");

        var req = new HttpRequestMessage(HttpMethod.Get, $"/api/client/files/{video.Id}/content");
        req.Headers.Range = new RangeHeaderValue(100, 199);
        var resp = await alice.Http.SendAsync(req);

        Assert.Equal(HttpStatusCode.PartialContent, resp.StatusCode);
        Assert.Equal("bytes 100-199/5000", resp.Content.Headers.ContentRange!.ToString());
        Assert.Equal("video/mp4", resp.Content.Headers.ContentType!.MediaType);
        Assert.Equal("bytes", resp.Headers.AcceptRanges.Single());
        var body = await resp.Content.ReadAsByteArrayAsync();
        Assert.Equal(100, body.Length);
        Assert.Equal((byte)(100 % 251), body[0]);
    }

    [Fact]
    public async Task Download_mode_forces_attachment_with_original_name()
    {
        var alice = await app.LoginAsync("alice", TestApp.AlicePassword);
        var photo = (await app.FilesOfAsync(app.AlicePublished)).Single(f => f.Name == "IMG_2.jpg");

        var resp = await alice.GetAsync($"/api/client/files/{photo.Id}/content?mode=download");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("attachment", resp.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal("IMG_2.jpg", resp.Content.Headers.ContentDisposition.FileNameStar);
        Assert.Equal(1000, (await resp.Content.ReadAsByteArrayAsync()).Length);
    }

    [Fact]
    public async Task Unsafe_types_are_never_served_inline()
    {
        var alice = await app.LoginAsync("alice", TestApp.AlicePassword);
        var svg = (await app.FilesOfAsync(app.AlicePublished)).Single(f => f.Name == "logo.svg");

        var resp = await alice.GetAsync($"/api/client/files/{svg.Id}/content?mode=stream");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("application/octet-stream", resp.Content.Headers.ContentType!.MediaType);
        Assert.Equal("attachment", resp.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Contains("sandbox", string.Join(";", resp.Headers.GetValues("Content-Security-Policy")));
    }

    [Fact]
    public async Task Downloads_disabled_blocks_download_but_allows_viewing()
    {
        var alice = await app.LoginAsync("alice", TestApp.AlicePassword);
        var photo = (await app.FilesOfAsync(app.AliceNoDownloads))[0];

        Assert.Equal(HttpStatusCode.Forbidden, (await alice.GetAsync($"/api/client/files/{photo.Id}/content?mode=download")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await alice.GetAsync($"/api/client/albums/{app.AliceNoDownloads.PublicId}/zip")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await alice.GetAsync($"/api/client/files/{photo.Id}/content?mode=stream")).StatusCode);
    }

    [Fact]
    public async Task Thumbnails_are_served_and_cached()
    {
        var alice = await app.LoginAsync("alice", TestApp.AlicePassword);
        var photo = (await app.FilesOfAsync(app.AlicePublished)).Single(f => f.Name == "IMG_10.jpg");

        var first = await alice.GetAsync($"/api/client/files/{photo.Id}/thumbnail?size=medium");
        var second = await alice.GetAsync($"/api/client/files/{photo.Id}/thumbnail?size=medium");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal("image/jpeg", first.Content.Headers.ContentType!.MediaType);
        Assert.Equal(await first.Content.ReadAsByteArrayAsync(), await second.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Section_zip_contains_the_sections_files()
    {
        var alice = await app.LoginAsync("alice", TestApp.AlicePassword);
        var resp = await alice.GetAsync($"/api/client/albums/{app.AlicePublished.PublicId}/zip?section=Photos");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("application/zip", resp.Content.Headers.ContentType!.MediaType);
        await using var stream = await resp.Content.ReadAsStreamAsync();
        using var zip = new System.IO.Compression.ZipArchive(stream);
        Assert.Equal(["IMG_2.jpg", "IMG_10.jpg"], zip.Entries.Select(e => e.FullName));
        Assert.All(zip.Entries, e => Assert.Equal(1000, e.Length));
    }
}
