using System.ComponentModel.DataAnnotations;
using FransStudios.Api.Authentication;
using FransStudios.Api.Configuration;
using FransStudios.Api.Data;
using FransStudios.Api.Entities;
using FransStudios.Api.Features.Public;
using FransStudios.Api.GoogleDrive;
using FransStudios.Api.Infrastructure;
using FransStudios.Api.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FransStudios.Api.Features.Admin;

// ─────────────────────────────────────────────────────────────────────────────
// Public portfolio galleries (images live in the PUBLIC media store, never Google Drive)
// ─────────────────────────────────────────────────────────────────────────────

public sealed record AdminGalleryListItem(int Id, string Title, string Slug, GalleryCategory Category, bool IsPublished,
    bool IsFeatured, int SortOrder, int ImageCount, ImageDto? Cover, DateTime UpdatedAt);

public sealed record AdminGalleryImage(int Id, ImageDto Image, string AltText, int SortOrder);

public sealed record AdminGalleryDetail(int Id, string Title, string Slug, GalleryCategory Category, string? Location,
    DateOnly? EventDate, string? Intro, string? SeoDescription, bool IsPublished, bool IsFeatured, int SortOrder,
    int? CoverImageId, IReadOnlyList<AdminGalleryImage> Images);

public sealed record SaveGalleryRequest(
    [Required, StringLength(200, MinimumLength = 1)] string Title,
    [Required, RegularExpression("^[a-z0-9]+(?:-[a-z0-9]+)*$", ErrorMessage = "Use lower-case letters, numbers and dashes."), StringLength(120)] string Slug,
    GalleryCategory Category,
    [StringLength(200)] string? Location,
    DateOnly? EventDate,
    [StringLength(4000)] string? Intro,
    [StringLength(300)] string? SeoDescription,
    bool IsPublished,
    bool IsFeatured,
    int SortOrder);

public sealed record AdminWallDto(int? Id, IReadOnlyList<AdminGalleryImage> Images);

public sealed record UpdateImageRequest([Required(AllowEmptyStrings = true), StringLength(300)] string AltText);
public sealed record ReorderRequest([Required] int[] Ids);
public sealed record SetImageRequest(int ImageId);

[ApiController]
[Route("api/admin/galleries")]
[Authorize(Policy = Policies.Admin)]
public sealed class AdminGalleriesController(AppDbContext db, PublicMediaStore store, IOptions<StorageOptions> storage) : ControllerBase
{
    /// <summary>Photos uploaded straight to the Portfolio wall (newest first). Id is null until the first upload.</summary>
    [HttpGet("wall")]
    public async Task<AdminWallDto> Wall(CancellationToken ct)
    {
        var wall = await db.PublicGalleries.AsNoTracking().Include(x => x.Images.OrderBy(i => i.SortOrder))
            .SingleOrDefaultAsync(x => x.Slug == PortfolioWall.Slug, ct);
        return new AdminWallDto(wall?.Id,
            wall?.Images.Select(i => new AdminGalleryImage(i.Id, ImageDto.From(i.Image, i.AltText, i.Id), i.AltText, i.SortOrder)).ToList() ?? []);
    }

    /// <summary>Upload photos straight to the Portfolio wall (multipart field "files"). No story needed.</summary>
    [HttpPost("wall/images")]
    [RequestSizeLimit(1_500_000_000)]
    [RequestFormLimits(MultipartBodyLengthLimit = 1_500_000_000)]
    public async Task<ActionResult<IReadOnlyList<AdminGalleryImage>>> UploadToWall([FromForm] IFormFileCollection files, CancellationToken ct)
    {
        var wall = await db.PublicGalleries.SingleOrDefaultAsync(x => x.Slug == PortfolioWall.Slug, ct);
        if (wall is null)
        {
            // Always published and first in wall order, so new uploads show at the top of the Portfolio page.
            wall = new PublicGallery { Title = PortfolioWall.Title, Slug = PortfolioWall.Slug, IsPublished = true, SortOrder = int.MinValue };
            db.PublicGalleries.Add(wall);
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException ex) when (ex.IsUniqueViolation())
            {
                // Created by a parallel upload a moment ago.
                db.ChangeTracker.Clear();
                wall = await db.PublicGalleries.SingleAsync(x => x.Slug == PortfolioWall.Slug, ct);
            }
        }
        return await SaveImagesAsync(wall, files, newestFirst: true, ct);
    }

    [HttpGet]
    public async Task<IReadOnlyList<AdminGalleryListItem>> List(CancellationToken ct)
    {
        var rows = await db.PublicGalleries.AsNoTracking().Where(g => g.Slug != PortfolioWall.Slug)
            .OrderBy(g => g.SortOrder).ThenByDescending(g => g.CreatedAt)
            .Select(g => new { g.Id, g.Title, g.Slug, g.Category, g.IsPublished, g.IsFeatured, g.SortOrder, g.UpdatedAt,
                Count = g.Images.Count, g.CoverImage, First = g.Images.OrderBy(i => i.SortOrder).FirstOrDefault() })
            .ToListAsync(ct);
        return rows.Select(r =>
        {
            var cover = r.CoverImage ?? r.First;
            return new AdminGalleryListItem(r.Id, r.Title, r.Slug, r.Category, r.IsPublished, r.IsFeatured, r.SortOrder, r.Count,
                cover is null ? null : ImageDto.From(cover.Image, cover.AltText), r.UpdatedAt);
        }).ToList();
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<AdminGalleryDetail>> Get(int id, CancellationToken ct)
    {
        var g = await db.PublicGalleries.AsNoTracking().Include(x => x.Images.OrderBy(i => i.SortOrder)).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (g is null) return this.NotFoundProblem("Gallery not found.");
        return new AdminGalleryDetail(g.Id, g.Title, g.Slug, g.Category, g.Location, g.EventDate, g.Intro, g.SeoDescription,
            g.IsPublished, g.IsFeatured, g.SortOrder, g.CoverImageId,
            g.Images.Select(i => new AdminGalleryImage(i.Id, ImageDto.From(i.Image, i.AltText, i.Id), i.AltText, i.SortOrder)).ToList());
    }

    [HttpPost]
    public async Task<ActionResult<AdminGalleryDetail>> Create(SaveGalleryRequest r, CancellationToken ct)
    {
        if (r.Slug == PortfolioWall.Slug) return this.ConflictProblem("That URL slug is reserved.");
        if (await db.PublicGalleries.AnyAsync(g => g.Slug == r.Slug, ct)) return this.ConflictProblem("That URL slug is already used.");
        var g = new PublicGallery();
        Apply(g, r);
        db.PublicGalleries.Add(g);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation()) { return this.ConflictProblem("That URL slug is already used."); }
        return CreatedAtAction(nameof(Get), new { id = g.Id }, (await Get(g.Id, ct)).Value);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, SaveGalleryRequest r, CancellationToken ct)
    {
        var g = await db.PublicGalleries.SingleOrDefaultAsync(x => x.Id == id && x.Slug != PortfolioWall.Slug, ct);
        if (g is null) return this.NotFoundProblem("Gallery not found.");
        if (r.Slug == PortfolioWall.Slug) return this.ConflictProblem("That URL slug is reserved.");
        if (await db.PublicGalleries.AnyAsync(x => x.Slug == r.Slug && x.Id != id, ct)) return this.ConflictProblem("That URL slug is already used.");
        Apply(g, r);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var g = await db.PublicGalleries.Include(x => x.Images).SingleOrDefaultAsync(x => x.Id == id && x.Slug != PortfolioWall.Slug, ct);
        if (g is null) return this.NotFoundProblem("Gallery not found.");
        var keys = g.Images.Select(i => i.Image.Key).ToList();
        g.CoverImageId = null;
        await db.SaveChangesAsync(ct);
        db.PublicImages.RemoveRange(g.Images);
        db.PublicGalleries.Remove(g);
        await db.SaveChangesAsync(ct);
        keys.ForEach(store.TryDelete);
        return NoContent();
    }

    /// <summary>Upload one or more images (multipart field "files"). Each is converted to WebP/JPEG variants.</summary>
    [HttpPost("{id:int}/images")]
    [RequestSizeLimit(1_500_000_000)]
    [RequestFormLimits(MultipartBodyLengthLimit = 1_500_000_000)]
    public async Task<ActionResult<IReadOnlyList<AdminGalleryImage>>> Upload(int id, [FromForm] IFormFileCollection files, CancellationToken ct)
    {
        var g = await db.PublicGalleries.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (g is null) return this.NotFoundProblem("Gallery not found.");
        return await SaveImagesAsync(g, files, newestFirst: g.Slug == PortfolioWall.Slug, ct);
    }

    /// <param name="newestFirst">Put new photos before the existing ones (the wall) instead of after (stories).</param>
    private async Task<ActionResult<IReadOnlyList<AdminGalleryImage>>> SaveImagesAsync(PublicGallery g, IFormFileCollection files, bool newestFirst, CancellationToken ct)
    {
        if (files.Count == 0) return this.FieldError("files", "Choose at least one image.");

        var max = storage.Value.MaxImageUploadMb * 1024L * 1024;
        var existing = db.PublicImages.Where(i => i.GalleryId == g.Id);
        // Newest first: the batch takes the slots just below the current minimum, keeping its own order.
        var next = newestFirst
            ? (await existing.MinAsync(i => (int?)i.SortOrder, ct) ?? 0) - files.Count
            : (await existing.MaxAsync(i => (int?)i.SortOrder, ct) ?? -1) + 1;
        var added = new List<PublicImage>();
        foreach (var file in files)
        {
            if (file.Length > max) return this.FieldError("files", $"{file.FileName} is larger than {storage.Value.MaxImageUploadMb} MB.");
            ImageAsset asset;
            try
            {
                await using var s = file.OpenReadStream();
                asset = await store.SaveImageAsync(s, ct);
            }
            catch (InvalidUploadException ex) { return this.FieldError("files", $"{file.FileName}: {ex.Message}"); }

            var img = new PublicImage { GalleryId = g.Id, Image = asset, AltText = "", SortOrder = next++ };
            db.PublicImages.Add(img);
            added.Add(img);
            await db.SaveChangesAsync(ct); // save per image so a later failure keeps earlier work
        }
        return added.Select(i => new AdminGalleryImage(i.Id, ImageDto.From(i.Image, i.AltText, i.Id), i.AltText, i.SortOrder)).ToList();
    }

    [HttpPut("{id:int}/images/{imageId:int}")]
    public async Task<IActionResult> UpdateImage(int id, int imageId, UpdateImageRequest r, CancellationToken ct)
    {
        var img = await db.PublicImages.SingleOrDefaultAsync(i => i.Id == imageId && i.GalleryId == id, ct);
        if (img is null) return this.NotFoundProblem("Image not found.");
        img.AltText = r.AltText.Trim();
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPut("{id:int}/images/order")]
    public async Task<IActionResult> Reorder(int id, ReorderRequest r, CancellationToken ct)
    {
        var images = await db.PublicImages.Where(i => i.GalleryId == id).ToListAsync(ct);
        var position = r.Ids.Select((imageId, idx) => (imageId, idx)).ToDictionary(x => x.imageId, x => x.idx);
        foreach (var img in images) img.SortOrder = position.GetValueOrDefault(img.Id, images.Count + img.SortOrder);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete("{id:int}/images/{imageId:int}")]
    public async Task<IActionResult> DeleteImage(int id, int imageId, CancellationToken ct)
    {
        var img = await db.PublicImages.SingleOrDefaultAsync(i => i.Id == imageId && i.GalleryId == id, ct);
        if (img is null) return this.NotFoundProblem("Image not found.");
        await db.PublicGalleries.Where(g => g.Id == id && g.CoverImageId == imageId)
            .ExecuteUpdateAsync(u => u.SetProperty(g => g.CoverImageId, (int?)null), ct);
        db.PublicImages.Remove(img);
        await db.SaveChangesAsync(ct);
        store.TryDelete(img.Image.Key);
        return NoContent();
    }

    [HttpPut("{id:int}/cover")]
    public async Task<IActionResult> SetCover(int id, SetImageRequest r, CancellationToken ct)
    {
        var g = await db.PublicGalleries.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (g is null) return this.NotFoundProblem("Gallery not found.");
        if (!await db.PublicImages.AnyAsync(i => i.Id == r.ImageId && i.GalleryId == id, ct)) return this.FieldError("imageId", "Image not in this gallery.");
        g.CoverImageId = r.ImageId;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private static void Apply(PublicGallery g, SaveGalleryRequest r)
    {
        g.Title = r.Title.Trim();
        g.Slug = r.Slug;
        g.Category = r.Category;
        g.Location = Clean(r.Location);
        g.EventDate = r.EventDate;
        g.Intro = Clean(r.Intro);
        g.SeoDescription = Clean(r.SeoDescription);
        g.IsPublished = r.IsPublished;
        g.IsFeatured = r.IsFeatured;
        g.SortOrder = r.SortOrder;
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

// ─────────────────────────────────────────────────────────────────────────────
// Public films
// ─────────────────────────────────────────────────────────────────────────────

public sealed record AdminFilmDto(int Id, string Title, string? Description, ImageDto? Poster, string? VideoUrl,
    long? VideoBytes, bool IsPublished, int SortOrder);

public sealed record SaveFilmRequest(
    [Required, StringLength(200, MinimumLength = 1)] string Title,
    [StringLength(2000)] string? Description,
    bool IsPublished,
    int SortOrder);

[ApiController]
[Route("api/admin/films")]
[Authorize(Policy = Policies.Admin)]
public sealed class AdminFilmsController(AppDbContext db, PublicMediaStore store, IOptions<StorageOptions> storage) : ControllerBase
{
    [HttpGet]
    public async Task<IReadOnlyList<AdminFilmDto>> List(CancellationToken ct) =>
        (await db.PublicFilms.AsNoTracking().OrderBy(f => f.SortOrder).ToListAsync(ct)).Select(ToDto).ToList();

    [HttpPost]
    public async Task<ActionResult<AdminFilmDto>> Create(SaveFilmRequest r, CancellationToken ct)
    {
        var f = new PublicFilm { Title = r.Title.Trim(), Description = r.Description?.Trim(), IsPublished = r.IsPublished, SortOrder = r.SortOrder };
        db.PublicFilms.Add(f);
        await db.SaveChangesAsync(ct);
        return ToDto(f);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, SaveFilmRequest r, CancellationToken ct)
    {
        var f = await db.PublicFilms.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (f is null) return this.NotFoundProblem("Film not found.");
        // A poster alone is enough: the site shows the film as "coming soon" until the video is uploaded.
        if (r.IsPublished && f.VideoKey is null && f.Poster is null)
            return this.FieldError("isPublished", "Upload a poster image or the video before publishing.");
        f.Title = r.Title.Trim();
        f.Description = string.IsNullOrWhiteSpace(r.Description) ? null : r.Description.Trim();
        f.IsPublished = r.IsPublished;
        f.SortOrder = r.SortOrder;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("{id:int}/poster")]
    [RequestSizeLimit(200_000_000)]
    public async Task<ActionResult<AdminFilmDto>> Poster(int id, IFormFile file, CancellationToken ct)
    {
        var f = await db.PublicFilms.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (f is null) return this.NotFoundProblem("Film not found.");
        if (file.Length > storage.Value.MaxImageUploadMb * 1024L * 1024) return this.FieldError("file", "Image is too large.");
        try
        {
            await using var s = file.OpenReadStream();
            var old = f.Poster?.Key;
            f.Poster = await store.SaveImageAsync(s, ct);
            await db.SaveChangesAsync(ct);
            store.TryDelete(old);
        }
        catch (InvalidUploadException ex) { return this.FieldError("file", ex.Message); }
        return ToDto(f);
    }

    /// <summary>Upload the web version of a film (MP4 H.264 with "fast start", ideally 1080p).</summary>
    [HttpPost("{id:int}/video")]
    [RequestSizeLimit(4_200_000_000)]
    [RequestFormLimits(MultipartBodyLengthLimit = 4_200_000_000)]
    public async Task<ActionResult<AdminFilmDto>> Video(int id, IFormFile file, CancellationToken ct)
    {
        var f = await db.PublicFilms.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (f is null) return this.NotFoundProblem("Film not found.");
        if (file.Length > storage.Value.MaxVideoUploadMb * 1024L * 1024)
            return this.FieldError("file", $"Video is larger than {storage.Value.MaxVideoUploadMb} MB. Export a 1080p web version.");
        try
        {
            await using var s = file.OpenReadStream();
            var (key, bytes) = await store.SaveVideoAsync(s, file.ContentType, ct);
            var old = f.VideoKey;
            f.VideoKey = key;
            f.VideoBytes = bytes;
            f.VideoMimeType = file.ContentType.ToLowerInvariant();
            await db.SaveChangesAsync(ct);
            store.TryDelete(old);
        }
        catch (InvalidUploadException ex) { return this.FieldError("file", ex.Message); }
        return ToDto(f);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var f = await db.PublicFilms.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (f is null) return this.NotFoundProblem("Film not found.");
        db.PublicFilms.Remove(f);
        await db.SaveChangesAsync(ct);
        store.TryDelete(f.VideoKey);
        store.TryDelete(f.Poster?.Key);
        return NoContent();
    }

    private static AdminFilmDto ToDto(PublicFilm f)
    {
        var dto = PublicController.ToFilmDto(f);
        return new AdminFilmDto(f.Id, f.Title, f.Description, dto.Poster, dto.VideoUrl, f.VideoBytes, f.IsPublished, f.SortOrder);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Packages (optional; the Packages page stays an inquiry form until one is published)
// ─────────────────────────────────────────────────────────────────────────────

public sealed record AdminPackageDto(int Id, string Name, string? Summary, decimal? Price, string? Currency, string? PriceNote,
    string? Features, bool IsPublished, int SortOrder);

public sealed record SavePackageRequest(
    [Required, StringLength(120, MinimumLength = 1)] string Name,
    [StringLength(1000)] string? Summary,
    [Range(0, 100_000_000)] decimal? Price,
    [RegularExpression("^[A-Z]{3}$", ErrorMessage = "Use a 3-letter currency code, e.g. EGP.")] string? Currency,
    [StringLength(120)] string? PriceNote,
    [StringLength(4000)] string? Features,
    bool IsPublished,
    int SortOrder);

[ApiController]
[Route("api/admin/packages")]
[Authorize(Policy = Policies.Admin)]
public sealed class AdminPackagesController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IReadOnlyList<AdminPackageDto>> List(CancellationToken ct) =>
        await db.Packages.AsNoTracking().OrderBy(p => p.SortOrder)
            .Select(p => new AdminPackageDto(p.Id, p.Name, p.Summary, p.Price, p.Currency, p.PriceNote, p.Features, p.IsPublished, p.SortOrder))
            .ToListAsync(ct);

    [HttpPost]
    public async Task<ActionResult<int>> Create(SavePackageRequest r, CancellationToken ct)
    {
        var p = new Package();
        Apply(p, r);
        db.Packages.Add(p);
        await db.SaveChangesAsync(ct);
        return p.Id;
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, SavePackageRequest r, CancellationToken ct)
    {
        var p = await db.Packages.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return this.NotFoundProblem("Package not found.");
        Apply(p, r);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var deleted = await db.Packages.Where(x => x.Id == id).ExecuteDeleteAsync(ct);
        return deleted == 0 ? this.NotFoundProblem("Package not found.") : NoContent();
    }

    private static void Apply(Package p, SavePackageRequest r)
    {
        p.Name = r.Name.Trim();
        p.Summary = r.Summary?.Trim();
        p.Price = r.Price;
        p.Currency = r.Currency;
        p.PriceNote = r.PriceNote?.Trim();
        p.Features = r.Features?.Trim();
        p.IsPublished = r.IsPublished;
        p.SortOrder = r.SortOrder;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Inquiries, dashboard, audit
// ─────────────────────────────────────────────────────────────────────────────

public sealed record UpdateInquiryRequest(InquiryStatus Status);

public sealed record DashboardDto(int ActiveClients, int PublishedAlbums, int DraftAlbums, int NewInquiries,
    int AlbumsWithSyncErrors, bool DriveConnected, string? DriveAccount, IReadOnlyList<Inquiry> RecentInquiries);

public sealed record AuditDto(long Id, string Action, string? Username, string? EntityType, string? EntityId, string? IpAddress,
    string? Details, DateTime CreatedAt);

public sealed record InquiryCountsDto(int PackagesNew, int ContactNew, int PackagesTotal, int ContactTotal);

[ApiController]
[Route("api/admin")]
[Authorize(Policy = Policies.Admin)]
public sealed class AdminOverviewController(AppDbContext db, DriveTokenService drive) : ControllerBase
{
    [HttpGet("dashboard")]
    public async Task<DashboardDto> Dashboard(CancellationToken ct)
    {
        var connection = await drive.GetActiveConnectionAsync(ct);
        return new DashboardDto(
            await db.Clients.CountAsync(c => c.IsActive, ct),
            await db.Albums.CountAsync(a => a.Status == AlbumStatus.Published, ct),
            await db.Albums.CountAsync(a => a.Status == AlbumStatus.Draft, ct),
            await db.Inquiries.CountAsync(i => i.Status == InquiryStatus.New, ct),
            await db.Albums.CountAsync(a => a.LastSyncError != null, ct),
            connection is not null,
            connection?.AccountEmail,
            await db.Inquiries.AsNoTracking().OrderByDescending(i => i.CreatedAt).Take(5).ToListAsync(ct));
    }

    /// <summary>Requests sent from the website, optionally only one form's (origin=packages|contact).</summary>
    [HttpGet("inquiries")]
    public Task<Paged<Inquiry>> Inquiries(InquiryStatus? status, InquiryOrigin? origin, int page = 1, int pageSize = 25, CancellationToken ct = default)
    {
        var q = db.Inquiries.AsNoTracking();
        if (status is not null) q = q.Where(i => i.Status == status);
        if (origin is not null) q = q.Where(i => i.Origin == origin);
        return q.OrderByDescending(i => i.CreatedAt).ToPagedAsync(page, pageSize, ct);
    }

    /// <summary>Counts for the Requests tabs and the menu badge.</summary>
    [HttpGet("inquiries/counts")]
    public async Task<InquiryCountsDto> InquiryCounts(CancellationToken ct)
    {
        var rows = await db.Inquiries.AsNoTracking()
            .GroupBy(i => new { i.Origin, IsNew = i.Status == InquiryStatus.New })
            .Select(g => new { g.Key.Origin, g.Key.IsNew, Count = g.Count() })
            .ToListAsync(ct);
        int Count(InquiryOrigin o, bool? isNew = null) => rows.Where(r => r.Origin == o && (isNew is null || r.IsNew == isNew)).Sum(r => r.Count);
        return new InquiryCountsDto(Count(InquiryOrigin.Packages, true), Count(InquiryOrigin.Contact, true),
            Count(InquiryOrigin.Packages), Count(InquiryOrigin.Contact));
    }

    [HttpPut("inquiries/{id:int}")]
    public async Task<IActionResult> UpdateInquiry(int id, UpdateInquiryRequest r, CancellationToken ct)
    {
        var n = await db.Inquiries.Where(i => i.Id == id).ExecuteUpdateAsync(u => u.SetProperty(i => i.Status, r.Status), ct);
        return n == 0 ? this.NotFoundProblem("Inquiry not found.") : NoContent();
    }

    [HttpDelete("inquiries/{id:int}")]
    public async Task<IActionResult> DeleteInquiry(int id, CancellationToken ct)
    {
        var n = await db.Inquiries.Where(i => i.Id == id).ExecuteDeleteAsync(ct);
        return n == 0 ? this.NotFoundProblem("Inquiry not found.") : NoContent();
    }

    [HttpGet("audit")]
    public Task<Paged<AuditDto>> Audit(string? action, int? userId, int page = 1, int pageSize = 50, CancellationToken ct = default)
    {
        var q = db.AuditLogs.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(action)) q = q.Where(a => a.Action.StartsWith(action));
        if (userId is not null) q = q.Where(a => a.UserId == userId);
        return q.OrderByDescending(a => a.Id)
            .Select(a => new AuditDto(a.Id, a.Action, a.User != null ? a.User.Username : null, a.EntityType, a.EntityId, a.IpAddress, a.Details, a.CreatedAt))
            .ToPagedAsync(page, pageSize, ct);
    }
}
