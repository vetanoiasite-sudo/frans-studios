using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FransStudios.Api.Tests.Infrastructure;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace FransStudios.Api.Tests;

public sealed class PortfolioWallTests(TestApp app) : IClassFixture<TestApp>
{
    [Fact]
    public async Task Wall_lists_photos_of_published_albums_only_in_album_order()
    {
        var admin = await app.LoginAsync("admin", TestApp.AdminPassword);
        var hidden = await CreateGalleryAsync(admin, "Hidden Story", "hidden-story", published: false, sortOrder: 0);
        var second = await CreateGalleryAsync(admin, "Second Story", "second-story", published: true, sortOrder: 20);
        var first = await CreateGalleryAsync(admin, "First Story", "first-story", published: true, sortOrder: 10);
        await UploadAsync(admin, hidden);
        await UploadAsync(admin, second);
        await UploadAsync(admin, first);

        var wall = await app.NewSession().Http.GetFromJsonAsync<JsonElement>("/api/public/portfolio?pageSize=50");
        var slugs = wall.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("gallerySlug").GetString()).ToList();

        Assert.Equal(["first-story", "second-story"], slugs);
        Assert.Equal(2, wall.GetProperty("total").GetInt32());
    }

    internal static async Task<int> CreateGalleryAsync(ApiSession admin, string title, string slug, bool published, int sortOrder)
    {
        var resp = await admin.SendAsync(HttpMethod.Post, "/api/admin/galleries",
            new { title, slug, category = "wedding", isPublished = published, isFeatured = false, sortOrder });
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
    }

    private static Task UploadAsync(ApiSession admin, int galleryId) => UploadToAsync(admin, $"/api/admin/galleries/{galleryId}/images");

    internal static async Task UploadToAsync(ApiSession admin, string url, int count = 1)
    {
        var form = new MultipartFormDataContent();
        for (var n = 0; n < count; n++)
        {
            using var jpg = new MemoryStream();
            using (var image = new Image<Rgb24>(300, 450, new Rgb24(120, 110, 100))) await image.SaveAsJpegAsync(jpg);
            form.Add(new ByteArrayContent(jpg.ToArray()), "files", $"photo{n}.jpg");
        }
        var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = form };
        req.Headers.Add("X-XSRF-TOKEN", admin.Cookie("XSRF-TOKEN"));
        Assert.Equal(HttpStatusCode.OK, (await admin.Http.SendAsync(req)).StatusCode);
    }
}

/// <summary>Admin → Portfolio → "Upload photos": straight onto the wall, no story.</summary>
public sealed class PortfolioDirectUploadTests(TestApp app) : IClassFixture<TestApp>
{
    [Fact]
    public async Task Direct_uploads_lead_the_wall_newest_first_and_never_show_up_as_a_story()
    {
        var admin = await app.LoginAsync("admin", TestApp.AdminPassword);
        var story = await PortfolioWallTests.CreateGalleryAsync(admin, "A Story", "a-story", published: true, sortOrder: 0);
        await PortfolioWallTests.UploadToAsync(admin, $"/api/admin/galleries/{story}/images");

        await PortfolioWallTests.UploadToAsync(admin, "/api/admin/galleries/wall/images", count: 2); // older batch
        await PortfolioWallTests.UploadToAsync(admin, "/api/admin/galleries/wall/images");           // newest

        var adminWall = await admin.Http.GetFromJsonAsync<JsonElement>("/api/admin/galleries/wall");
        var wallIds = adminWall.GetProperty("images").EnumerateArray().Select(i => i.GetProperty("id").GetInt32()).ToList();
        Assert.Equal(3, wallIds.Count);
        Assert.Equal(wallIds.Max(), wallIds[0]); // the last upload comes first

        var visitor = app.NewSession().Http;
        var wall = await visitor.GetFromJsonAsync<JsonElement>("/api/public/portfolio?pageSize=50");
        var items = wall.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(4, items.Count);
        Assert.Equal(wallIds, items.Take(3).Select(i => i.GetProperty("image").GetProperty("id").GetInt32()).ToList());
        Assert.Equal("a-story", items[3].GetProperty("gallerySlug").GetString());

        // Not a story: absent from story lists, its page, the admin story list and the sitemap; slug is reserved.
        var stories = await visitor.GetFromJsonAsync<JsonElement>("/api/public/galleries");
        Assert.Equal(["a-story"], stories.EnumerateArray().Select(g => g.GetProperty("slug").GetString()).ToList());
        Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync("/api/public/galleries/portfolio-wall")).StatusCode);
        var adminStories = await admin.Http.GetFromJsonAsync<JsonElement>("/api/admin/galleries");
        Assert.DoesNotContain(adminStories.EnumerateArray(), g => g.GetProperty("slug").GetString() == "portfolio-wall");
        Assert.DoesNotContain("portfolio-wall", await visitor.GetStringAsync("/sitemap.xml"));
        var reserved = await admin.SendAsync(HttpMethod.Post, "/api/admin/galleries",
            new { title = "X", slug = "portfolio-wall", category = "wedding", isPublished = true, isFeatured = false, sortOrder = 0 });
        Assert.Equal(HttpStatusCode.Conflict, reserved.StatusCode);
    }
}
