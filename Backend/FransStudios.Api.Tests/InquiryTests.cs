using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FransStudios.Api.Tests.Infrastructure;

namespace FransStudios.Api.Tests;

/// <summary>Website requests remember which form sent them (Packages or Contact), for the admin's two tabs.</summary>
public sealed class InquiryTests(TestApp app) : IClassFixture<TestApp>
{
    [Fact]
    public async Task Requests_are_split_by_the_form_that_sent_them()
    {
        var visitor = app.NewSession();
        await visitor.EnsureCsrfAsync();
        Assert.Equal(HttpStatusCode.Accepted, (await visitor.SendAsync(HttpMethod.Post, "/api/public/inquiries",
            new { name = "Packages Person", mobile = "+201000000001", coverage = "Full day", venue = "Hall, Cairo, Egypt", form = "packages" })).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await visitor.SendAsync(HttpMethod.Post, "/api/public/inquiries",
            new { name = "Contact Person", mobile = "+201000000002", venue = "Hotel, Vienna, Austria", form = "contact" })).StatusCode);

        var admin = await app.LoginAsync("admin", TestApp.AdminPassword);
        var packages = await Json(await admin.GetAsync("/api/admin/inquiries?origin=packages"));
        var contact = await Json(await admin.GetAsync("/api/admin/inquiries?origin=contact"));
        Assert.Equal(["Packages Person"], packages.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("name").GetString()));
        Assert.Equal(["Contact Person"], contact.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("name").GetString()));
        Assert.Equal("contact", contact.GetProperty("items")[0].GetProperty("origin").GetString());

        var counts = await Json(await admin.GetAsync("/api/admin/inquiries/counts"));
        Assert.Equal(1, counts.GetProperty("packagesNew").GetInt32());
        Assert.Equal(1, counts.GetProperty("contactNew").GetInt32());

        // Handling a request takes it out of the "new" count.
        var id = packages.GetProperty("items")[0].GetProperty("id").GetInt32();
        (await admin.SendAsync(HttpMethod.Put, $"/api/admin/inquiries/{id}", new { status = "contacted" })).EnsureSuccessStatusCode();
        counts = await Json(await admin.GetAsync("/api/admin/inquiries/counts"));
        Assert.Equal(0, counts.GetProperty("packagesNew").GetInt32());
        Assert.Equal(1, counts.GetProperty("packagesTotal").GetInt32());
    }

    private static async Task<JsonElement> Json(HttpResponseMessage resp) => await resp.Content.ReadFromJsonAsync<JsonElement>();
}
