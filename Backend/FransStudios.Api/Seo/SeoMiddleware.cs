using System.Net;
using System.Text;
using System.Text.Json;
using FransStudios.Api.Configuration;
using FransStudios.Api.Data;
using FransStudios.Api.Entities;
using FransStudios.Api.Features.Public;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FransStudios.Api.Seo;

/// <summary>
/// Serves the React app's index.html for every client-side route, with the page's &lt;title&gt;,
/// description, canonical URL, Open Graph tags and JSON-LD injected server-side (link-preview bots
/// for WhatsApp/Facebook/Instagram don't run JavaScript). Also applies legacy WordPress 301s and
/// marks /admin and /client as noindex.
/// </summary>
public sealed class SeoMiddleware(RequestDelegate next, IWebHostEnvironment env, IOptions<SiteOptions> siteOptions, ILogger<SeoMiddleware> logger)
{
    private const string Placeholder = "<!--app-head-->";
    private string? _template;
    private DateTime _templateStamp;

    private sealed record PageMeta(string Title, string Description, string? Image = null, string? JsonLd = null,
        bool NoIndex = false, int Status = 200);

    private static readonly Dictionary<string, (string Title, string Description)> StaticPages = new(StringComparer.OrdinalIgnoreCase)
    {
        ["/"] = ("Frans Studios — Wedding Photography & Cinematography",
            "Documentary and artistic wedding photography and films by Joe Frans and team. Natural, honest, timeless — with a love for film tones and black & white."),
        ["/about"] = ("About Us — Frans Studios",
            "Meet Joe Frans and the Frans Studios team: wedding photographers and cinematographers who tell real stories with calm, unobtrusive presence."),
        ["/portfolio"] = ("Portfolio — Frans Studios",
            "Wedding stories photographed by Frans Studios: real moments, film tones and timeless black & white."),
        ["/films"] = ("Wedding Films — Frans Studios",
            "Cinematic wedding films by Frans Studios — every glance, laugh and embrace woven into a story."),
        ["/packages"] = ("Packages — Frans Studios",
            "Ask about Frans Studios wedding photography and film packages. Tell us about your day and we'll send our offers."),
        ["/contact"] = ("Contact — Frans Studios",
            "Get in touch with Frans Studios about your wedding date, venue and coverage. Email, WhatsApp or the inquiry form."),
    };

    public async Task InvokeAsync(HttpContext ctx, AppDbContext db)
    {
        var req = ctx.Request;
        if (!HttpMethods.IsGet(req.Method) && !HttpMethods.IsHead(req.Method)
            || req.Path.StartsWithSegments("/api") || req.Path.StartsWithSegments("/media")
            || Path.HasExtension(req.Path.Value))
        {
            await next(ctx);
            return;
        }

        var site = siteOptions.Value;
        var path = NormalizePath(req.Path.Value);

        // 1) Old WordPress URLs → new routes (keeps rankings and existing Instagram/WhatsApp links working).
        if (site.LegacyRedirects.TryGetValue(path, out var target) || site.LegacyRedirects.TryGetValue(Uri.UnescapeDataString(path), out target))
        {
            ctx.Response.Redirect(target, permanent: true);
            return;
        }
        if (!StaticPages.ContainsKey(path) && IsSingleSegment(path) && !IsAppArea(path))
        {
            var slug = path[1..];
            if (await db.PublicGalleries.PublishedStories().AnyAsync(g => g.Slug == slug, ctx.RequestAborted))
            {
                ctx.Response.Redirect($"/portfolio/{slug}", permanent: true);
                return;
            }
        }

        // 2) Work out the page's metadata.
        var meta = await ResolveAsync(path, db, site, ctx.RequestAborted);

        var template = LoadTemplate();
        if (template is null)
        {
            ctx.Response.StatusCode = 503;
            await ctx.Response.WriteAsync("Frontend build not found. Run the frontend build (see docs/DEPLOYMENT.md).");
            return;
        }

        ctx.Response.StatusCode = meta.Status;
        ctx.Response.ContentType = "text/html; charset=utf-8";
        ctx.Response.Headers.CacheControl = "no-cache";
        if (meta.NoIndex) ctx.Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
        await ctx.Response.WriteAsync(template.Replace(Placeholder, RenderHead(meta, path, site)), ctx.RequestAborted);
    }

    private static async Task<PageMeta> ResolveAsync(string path, AppDbContext db, SiteOptions site, CancellationToken ct)
    {
        if (IsAppArea(path))
            return new PageMeta(path.StartsWith("/admin", StringComparison.OrdinalIgnoreCase) ? "Admin — Frans Studios" : "Your Gallery — Frans Studios",
                "Private area.", NoIndex: true);

        if (StaticPages.TryGetValue(path, out var page))
        {
            var jsonLd = path == "/" ? BusinessJsonLd(site) : null;
            var image = path == "/" ? await FeaturedImageAsync(db, ct) : null;
            return new PageMeta(page.Title, page.Description, image, jsonLd);
        }

        if (path.StartsWith("/portfolio/", StringComparison.OrdinalIgnoreCase) && IsSingleSegment(path["/portfolio".Length..]))
        {
            var slug = path["/portfolio/".Length..];
            var g = await db.PublicGalleries.AsNoTracking().PublishedStories()
                .Where(x => x.Slug == slug)
                .Select(x => new
                {
                    x.Title, x.Intro, x.SeoDescription, x.Location,
                    x.CoverImage,
                    Images = x.Images.OrderBy(i => i.SortOrder).Take(12).ToList(),
                })
                .SingleOrDefaultAsync(ct);
            if (g is not null)
            {
                var description = g.SeoDescription ?? Shorten(g.Intro) ??
                                  $"{g.Title}{(g.Location is null ? "" : " in " + g.Location)} — a wedding story by Frans Studios.";
                var coverImage = g.CoverImage ?? g.Images.FirstOrDefault();
                var cover = coverImage is null ? null : ShareImage(coverImage.Image);
                var jsonLd = JsonSerializer.Serialize(new Dictionary<string, object?>
                {
                    ["@context"] = "https://schema.org",
                    ["@type"] = "ImageGallery",
                    ["name"] = g.Title,
                    ["description"] = description,
                    ["url"] = $"{site.PublicBaseUrl}/portfolio/{slug}",
                    ["image"] = g.Images.Select(i => Absolute(site, ShareImage(i.Image))).ToArray(),
                    ["author"] = new Dictionary<string, object?> { ["@type"] = "Organization", ["name"] = site.SiteName, ["url"] = site.PublicBaseUrl },
                });
                return new PageMeta($"{g.Title} — Frans Studios", description, cover, jsonLd);
            }
        }

        return new PageMeta("Page not found — Frans Studios", "This page doesn't exist.", NoIndex: true, Status: 404);
    }

    private static string RenderHead(PageMeta m, string path, SiteOptions site)
    {
        Func<string?, string?> e = WebUtility.HtmlEncode;
        var url = site.PublicBaseUrl.TrimEnd('/') + (path == "/" ? "/" : path);
        var image = m.Image ?? site.DefaultShareImage;
        var sb = new StringBuilder();
        sb.Append($"<title>{e(m.Title)}</title>\n");
        sb.Append($"<meta name=\"description\" content=\"{e(m.Description)}\">\n");
        if (m.NoIndex)
        {
            sb.Append("<meta name=\"robots\" content=\"noindex, nofollow\">\n");
            return sb.ToString();
        }
        sb.Append($"<link rel=\"canonical\" href=\"{e(url)}\">\n");
        sb.Append("<meta property=\"og:type\" content=\"website\">\n");
        sb.Append($"<meta property=\"og:site_name\" content=\"{e(site.SiteName)}\">\n");
        sb.Append($"<meta property=\"og:title\" content=\"{e(m.Title)}\">\n");
        sb.Append($"<meta property=\"og:description\" content=\"{e(m.Description)}\">\n");
        sb.Append($"<meta property=\"og:url\" content=\"{e(url)}\">\n");
        if (image is not null)
        {
            sb.Append($"<meta property=\"og:image\" content=\"{e(Absolute(site, image))}\">\n");
            sb.Append("<meta name=\"twitter:card\" content=\"summary_large_image\">\n");
        }
        if (m.JsonLd is not null)
            // JSON-LD is serialised by System.Text.Json, which escapes '<' so it can't close the script tag.
            sb.Append($"<script type=\"application/ld+json\">{m.JsonLd}</script>\n");
        return sb.ToString();
    }

    private static string BusinessJsonLd(SiteOptions site)
    {
        var b = site.Business;
        return JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "ProfessionalService",
            ["name"] = site.SiteName,
            ["description"] = "Wedding photography and cinematography.",
            ["url"] = site.PublicBaseUrl,
            ["email"] = b.Email,
            ["telephone"] = b.Telephone,
            ["image"] = site.DefaultShareImage is null ? null : Absolute(site, site.DefaultShareImage),
            ["sameAs"] = b.Instagram is null ? null : new[] { b.Instagram },
            ["address"] = b.CountryCode is null ? null : new Dictionary<string, object?> { ["@type"] = "PostalAddress", ["addressLocality"] = b.Locality, ["addressCountry"] = b.CountryCode },
        }.Where(kv => kv.Value is not null).ToDictionary());
    }

    private static async Task<string?> FeaturedImageAsync(AppDbContext db, CancellationToken ct)
    {
        var row = await db.PublicGalleries.AsNoTracking().Where(g => g.IsPublished)
            .OrderByDescending(g => g.IsFeatured).ThenBy(g => g.SortOrder)
            .Select(g => new { g.CoverImage, First = g.Images.OrderBy(i => i.SortOrder).FirstOrDefault() })
            .FirstOrDefaultAsync(ct);
        var img = row?.CoverImage ?? row?.First;
        return img is null ? null : ShareImage(img.Image);
    }

    /// <summary>A ~1600px JPEG — the format every link-preview bot accepts.</summary>
    private static string ShareImage(Entities.ImageAsset a)
    {
        var widths = a.Widths.Split(',').Select(int.Parse).ToArray();
        var w = widths.Where(x => x <= 1600).DefaultIfEmpty(widths.Min()).Max();
        return $"{ImageDto.From(a, "").Src}/{w}.jpg";
    }

    private static string Absolute(SiteOptions site, string url) =>
        url.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? url : site.PublicBaseUrl.TrimEnd('/') + url;

    private string? LoadTemplate()
    {
        var file = env.WebRootFileProvider.GetFileInfo("index.html");
        if (!file.Exists) return null;
        if (_template is null || file.LastModified.UtcDateTime != _templateStamp)
        {
            using var reader = new StreamReader(file.CreateReadStream());
            var html = reader.ReadToEnd();
            if (!html.Contains(Placeholder)) logger.LogWarning("index.html has no {Placeholder} marker; SEO tags won't be injected.", Placeholder);
            _template = html;
            _templateStamp = file.LastModified.UtcDateTime;
        }
        return _template;
    }

    private static string NormalizePath(string? path)
    {
        if (string.IsNullOrEmpty(path) || path == "/") return "/";
        return path.TrimEnd('/').ToLowerInvariant();
    }

    private static bool IsSingleSegment(string path) => path.Length > 1 && path.IndexOf('/', 1) < 0;

    private static bool IsAppArea(string path) =>
        path.StartsWith("/admin", StringComparison.OrdinalIgnoreCase) || path.StartsWith("/client", StringComparison.OrdinalIgnoreCase);

    private static string? Shorten(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        text = text.ReplaceLineEndings(" ").Trim();
        return text.Length <= 160 ? text : text[..157].TrimEnd() + "…";
    }
}

public static class SeoEndpoints
{
    public static void MapSeoEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/robots.txt", (IOptions<SiteOptions> site) => Results.Text(
            $"""
             User-agent: *
             Allow: /
             Disallow: /admin
             Disallow: /client
             Disallow: /api/

             Sitemap: {site.Value.PublicBaseUrl.TrimEnd('/')}/sitemap.xml
             """, "text/plain"));

        app.MapGet("/sitemap.xml", async (AppDbContext db, IOptions<SiteOptions> site, CancellationToken ct) =>
        {
            var root = site.Value.PublicBaseUrl.TrimEnd('/');
            var galleries = await db.PublicGalleries.AsNoTracking().PublishedStories()
                .OrderBy(g => g.SortOrder).Select(g => new { g.Slug, g.UpdatedAt }).ToListAsync(ct);

            var sb = new StringBuilder("""<?xml version="1.0" encoding="UTF-8"?>""" + "\n<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">\n");
            foreach (var p in new[] { "/", "/portfolio", "/films", "/about", "/packages", "/contact" })
                sb.Append($"  <url><loc>{root}{(p == "/" ? "/" : p)}</loc></url>\n");
            foreach (var g in galleries)
                sb.Append($"  <url><loc>{root}/portfolio/{WebUtility.HtmlEncode(g.Slug)}</loc><lastmod>{g.UpdatedAt:yyyy-MM-dd}</lastmod></url>\n");
            sb.Append("</urlset>\n");
            return Results.Text(sb.ToString(), "application/xml");
        });
    }
}
