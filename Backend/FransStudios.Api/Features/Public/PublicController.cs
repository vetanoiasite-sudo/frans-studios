using System.ComponentModel.DataAnnotations;
using FransStudios.Api.Configuration;
using FransStudios.Api.Data;
using FransStudios.Api.Entities;
using FransStudios.Api.Infrastructure;
using FransStudios.Api.Storage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FransStudios.Api.Features.Public;

/// <summary>A public image: the SPA builds srcset from "{src}/{width}.webp" and "{src}/{width}.jpg".</summary>
public sealed record ImageDto(int? Id, string Src, int Width, int Height, int[] Widths, string Alt)
{
    public static ImageDto From(ImageAsset a, string alt, int? id = null) =>
        new(id, $"{PublicMediaStore.RequestPath}/{a.Key}", a.Width, a.Height,
            a.Widths.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray(), alt);
}

public sealed record GalleryCard(string Slug, string Title, GalleryCategory Category, string? Location, DateOnly? EventDate,
    ImageDto? Cover, int ImageCount, bool IsFeatured);

public sealed record GalleryNav(string Slug, string Title);

public sealed record GalleryDetail(string Slug, string Title, GalleryCategory Category, string? Location, DateOnly? EventDate,
    string? Intro, ImageDto? Cover, IReadOnlyList<ImageDto> Images, GalleryNav? Previous, GalleryNav? Next);

public sealed record FilmDto(int Id, string Title, string? Description, ImageDto? Poster, string? VideoUrl, string? VideoMimeType);

public sealed record PackageDto(int Id, string Name, string? Summary, decimal? Price, string? Currency, string? PriceNote, string[] Features);

/// <summary>One photo on the Portfolio wall, with the album it belongs to.</summary>
public sealed record PortfolioImageDto(ImageDto Image, string GallerySlug, string GalleryTitle);

public sealed record InquiryRequest(
    [Required, StringLength(200, MinimumLength = 2)] string Name,
    [Required, StringLength(32, MinimumLength = 6)] string Mobile,
    [EmailAddress, StringLength(256)] string? Email,
    [StringLength(64)] string? EventType,
    DateOnly? EventDate,
    [StringLength(200)] string? Venue,
    [StringLength(32)] string? Coverage,
    [StringLength(64)] string? Source,
    [StringLength(4000)] string? Message,
    /// <summary>Honeypot: hidden field that humans leave empty.</summary>
    string? Website,
    /// <summary>Which page's form sent it: "packages" (default) or "contact".</summary>
    [StringLength(16)] string? Form = null);

[ApiController]
[Route("api/public")]
public sealed class PublicController(
    AppDbContext db,
    EmailDispatcher email,
    IOptions<EmailOptions> emailOptions,
    IOptions<SiteOptions> site) : ControllerBase
{
    [HttpGet("galleries")]
    [ResponseCache(Duration = 60, Location = ResponseCacheLocation.Any)]
    public async Task<IReadOnlyList<GalleryCard>> Galleries(GalleryCategory? category, bool? featured, CancellationToken ct)
    {
        var q = db.PublicGalleries.AsNoTracking().PublishedStories();
        if (category is not null) q = q.Where(g => g.Category == category);
        if (featured == true) q = q.Where(g => g.IsFeatured);
        var rows = await q.OrderBy(g => g.SortOrder).ThenByDescending(g => g.EventDate)
            .Select(g => new
            {
                g.Slug, g.Title, g.Category, g.Location, g.EventDate, g.IsFeatured,
                g.CoverImage,
                First = g.Images.OrderBy(i => i.SortOrder).FirstOrDefault(),
                Count = g.Images.Count,
            })
            .ToListAsync(ct);
        return rows.Select(r =>
        {
            var cover = r.CoverImage ?? r.First;
            return new GalleryCard(r.Slug, r.Title, r.Category, r.Location, r.EventDate,
                cover is null ? null : ImageDto.From(cover.Image, AltOr(cover.AltText, r.Title)), r.Count, r.IsFeatured);
        }).ToList();
    }

    [HttpGet("galleries/{slug}")]
    [ResponseCache(Duration = 60, Location = ResponseCacheLocation.Any)]
    public async Task<ActionResult<GalleryDetail>> Gallery(string slug, CancellationToken ct)
    {
        var g = await db.PublicGalleries.AsNoTracking()
            .Include(x => x.Images.OrderBy(i => i.SortOrder))
            .Include(x => x.CoverImage)
            .PublishedStories()
            .SingleOrDefaultAsync(x => x.Slug == slug, ct);
        if (g is null) return this.NotFoundProblem("Gallery not found.");

        var order = await db.PublicGalleries.AsNoTracking().PublishedStories()
            .OrderBy(x => x.SortOrder).ThenByDescending(x => x.EventDate)
            .Select(x => new GalleryNav(x.Slug, x.Title)).ToListAsync(ct);
        var idx = order.FindIndex(x => x.Slug == g.Slug);

        var images = g.Images.Select((i, n) => ImageDto.From(i.Image, AltOr(i.AltText, $"{g.Title} — photo {n + 1}"), i.Id)).ToList();
        var cover = g.CoverImage is { } c ? ImageDto.From(c.Image, AltOr(c.AltText, g.Title), c.Id) : images.FirstOrDefault();
        return new GalleryDetail(g.Slug, g.Title, g.Category, g.Location, g.EventDate, g.Intro, cover, images,
            idx > 0 ? order[idx - 1] : null, idx >= 0 && idx < order.Count - 1 ? order[idx + 1] : null);
    }

    /// <summary>
    /// The Portfolio wall: every photo of every published album, in album order (Admin → Portfolio sort order)
    /// then photo order. Paged so the page can load more as the visitor scrolls.
    /// </summary>
    [HttpGet("portfolio")]
    [ResponseCache(Duration = 60, Location = ResponseCacheLocation.Any)]
    public async Task<Paged<PortfolioImageDto>> Portfolio(int page = 1, int pageSize = 60, CancellationToken ct = default)
    {
        (page, pageSize) = ApiHelpers.Clamp(page, pageSize, 120);
        var q = db.PublicImages.AsNoTracking().Where(i => i.Gallery.IsPublished);
        var total = await q.CountAsync(ct);
        var rows = await q
            .OrderBy(i => i.Gallery.SortOrder).ThenBy(i => i.GalleryId).ThenBy(i => i.SortOrder).ThenBy(i => i.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(i => new { i.Id, i.Image, i.AltText, i.Gallery.Slug, i.Gallery.Title })
            .ToListAsync(ct);
        var items = rows.Select(r => new PortfolioImageDto(ImageDto.From(r.Image, AltOr(r.AltText, $"{r.Title} — wedding photography by Frans Studios"), r.Id), r.Slug, r.Title)).ToList();
        return new Paged<PortfolioImageDto>(items, total, page, pageSize);
    }

    [HttpGet("films")]
    [ResponseCache(Duration = 60, Location = ResponseCacheLocation.Any)]
    public async Task<IReadOnlyList<FilmDto>> Films(CancellationToken ct)
    {
        // Films may be published with only a poster ("coming soon"); videoUrl is null until the video is uploaded.
        var films = await db.PublicFilms.AsNoTracking().Where(f => f.IsPublished && (f.VideoKey != null || f.Poster != null))
            .OrderBy(f => f.SortOrder).ToListAsync(ct);
        return films.Select(ToFilmDto).ToList();
    }

    [HttpGet("packages")]
    [ResponseCache(Duration = 60, Location = ResponseCacheLocation.Any)]
    public async Task<IReadOnlyList<PackageDto>> Packages(CancellationToken ct)
    {
        var packages = await db.Packages.AsNoTracking().Where(p => p.IsPublished).OrderBy(p => p.SortOrder).ToListAsync(ct);
        return packages.Select(ToPackageDto).ToList();
    }

    /// <summary>Uploaded page covers by page key, e.g. { "packages": {…} }. Pages without one are left out.</summary>
    [HttpGet("page-covers")]
    [ResponseCache(Duration = 60, Location = ResponseCacheLocation.Any)]
    public async Task<IReadOnlyDictionary<string, ImageDto>> PageCovers(CancellationToken ct)
    {
        var covers = await db.PageCovers.AsNoTracking().ToListAsync(ct);
        return covers.ToDictionary(c => c.Page, c => ImageDto.From(c.Image, ""));
    }

    [HttpPost("inquiries")]
    [EnableRateLimiting(RateLimits.Inquiry)]
    public async Task<IActionResult> Inquiry(InquiryRequest request, CancellationToken ct)
    {
        // Bots fill every field. Pretend success so they don't adapt.
        if (!string.IsNullOrEmpty(request.Website)) return Accepted();

        var inquiry = new Inquiry
        {
            Name = request.Name.Trim(),
            Mobile = request.Mobile.Trim(),
            Email = Clean(request.Email),
            EventType = Clean(request.EventType),
            EventDate = request.EventDate,
            Venue = Clean(request.Venue),
            Coverage = Clean(request.Coverage),
            Source = Clean(request.Source),
            Message = Clean(request.Message),
            Origin = string.Equals(request.Form, "contact", StringComparison.OrdinalIgnoreCase) ? InquiryOrigin.Contact : InquiryOrigin.Packages,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
        };
        db.Inquiries.Add(inquiry);
        await db.SaveChangesAsync(ct);

        if (emailOptions.Value.NotifyAddress is { Length: > 0 } notify)
        {
            email.Enqueue(new EmailMessage(notify, $"New inquiry from {inquiry.Name}",
                $"""
                 Name: {inquiry.Name}
                 Mobile: {inquiry.Mobile}
                 Email: {inquiry.Email}
                 Event: {inquiry.EventType} on {inquiry.EventDate:yyyy-MM-dd}
                 Venue: {inquiry.Venue}
                 Coverage: {inquiry.Coverage}
                 Heard about us: {inquiry.Source}

                 {inquiry.Message}

                 View in the dashboard: {site.Value.PublicBaseUrl}/admin/inquiries
                 """, inquiry.Email));
        }
        return Accepted();
    }

    internal static FilmDto ToFilmDto(PublicFilm f) => new(f.Id, f.Title, f.Description,
        f.Poster is { Key.Length: > 0 } p ? ImageDto.From(p, f.Title) : null,
        f.VideoKey is null ? null : $"{PublicMediaStore.RequestPath}/{f.VideoKey}", f.VideoMimeType);

    internal static PackageDto ToPackageDto(Package p) => new(p.Id, p.Name, p.Summary, p.Price, p.Currency, p.PriceNote,
        (p.Features ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static string AltOr(string alt, string fallback) => string.IsNullOrWhiteSpace(alt) ? fallback : alt;
    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
