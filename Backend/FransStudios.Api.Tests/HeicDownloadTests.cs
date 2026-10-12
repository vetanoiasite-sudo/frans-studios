using System.Net;
using FransStudios.Api.Tests.Infrastructure;
using ImageMagick;

namespace FransStudios.Api.Tests;

/// <summary>HEIC/HEIF photos can be downloaded as full-size JPEGs; the original stays available and untouched.</summary>
public sealed class HeicDownloadTests(TestApp app) : IClassFixture<TestApp>
{
    /// <summary>
    /// A HEIF-container photo for the tests. Magick.NET reads HEIC but can't write it (no HEVC encoder), so the
    /// test image is AVIF — the same HEIF container, decoded by the same libheif path.
    /// </summary>
    private static byte[] MakeHeic(int width, int height)
    {
        using var image = new MagickImage(MagickColors.CornflowerBlue, (uint)width, (uint)height);
        image.Format = MagickFormat.Avif;
        return image.ToByteArray();
    }

    [Fact]
    public async Task Client_and_admin_can_download_heic_as_jpeg()
    {
        var heic = MakeHeic(64, 48);
        app.Drive.Items["fileAliceHeic001"] = new FakeItem("fileAliceHeic001", "IMG_0042.HEIC", "image/heif", "folderAlicePhot", heic);
        var admin = await app.LoginAsync("admin", TestApp.AdminPassword);
        (await admin.SendAsync(HttpMethod.Post, $"/api/admin/albums/{app.AlicePublished.Id}/sync")).EnsureSuccessStatusCode();
        var file = (await app.FilesOfAsync(app.AlicePublished)).Single(f => f.DriveFileId == "fileAliceHeic001");

        var alice = await app.LoginAsync("alice", TestApp.AlicePassword);
        var jpg = await alice.GetAsync($"/api/client/files/{file.Id}/content?mode=download&format=jpg");
        Assert.Equal(HttpStatusCode.OK, jpg.StatusCode);
        Assert.Equal("image/jpeg", jpg.Content.Headers.ContentType?.MediaType);
        Assert.Equal("IMG_0042.jpg", jpg.Content.Headers.ContentDisposition?.FileNameStar ?? jpg.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        var bytes = await jpg.Content.ReadAsByteArrayAsync();
        using (var decoded = new MagickImage(bytes))
        {
            Assert.Equal(MagickFormat.Jpeg, decoded.Format);
            Assert.Equal(64u, decoded.Width);
            Assert.Equal(48u, decoded.Height);
        }

        // Without format=jpg the original HEIC bytes come back unchanged.
        var original = await alice.GetAsync($"/api/client/files/{file.Id}/content?mode=download");
        Assert.Equal(heic, await original.Content.ReadAsByteArrayAsync());

        // format=jpg is ignored for files that aren't HEIC/HEIF.
        var png = (await app.FilesOfAsync(app.AlicePublished)).First(f => f.Name == "IMG_10.jpg");
        var same = await alice.GetAsync($"/api/client/files/{png.Id}/content?mode=download&format=jpg");
        Assert.Equal(1000, (await same.Content.ReadAsByteArrayAsync()).Length);

        // The admin Drive page offers the same conversion.
        var adminJpg = await admin.GetAsync("/api/admin/drive/items/fileAliceHeic001/content?download=true&format=jpg");
        Assert.Equal("image/jpeg", adminJpg.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Jpeg_download_respects_the_albums_download_setting()
    {
        app.Drive.Items["fileViewHeic0001"] = new FakeItem("fileViewHeic0001", "view.heic", "image/heic", "folderAliceView1", MakeHeic(32, 32));
        var admin = await app.LoginAsync("admin", TestApp.AdminPassword);
        (await admin.SendAsync(HttpMethod.Post, $"/api/admin/albums/{app.AliceNoDownloads.Id}/sync")).EnsureSuccessStatusCode();
        var file = (await app.FilesOfAsync(app.AliceNoDownloads)).Single(f => f.Name == "view.heic");

        var alice = await app.LoginAsync("alice", TestApp.AlicePassword);
        var refused = await alice.GetAsync($"/api/client/files/{file.Id}/content?mode=download&format=jpg");
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
    }
}
