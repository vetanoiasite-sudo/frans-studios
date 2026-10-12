using FransStudios.Api.Authentication;
using FransStudios.Api.Configuration;
using FransStudios.Api.Data;
using FransStudios.Api.Entities;
using FransStudios.Api.Features.Public;
using FransStudios.Api.Infrastructure;
using FransStudios.Api.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FransStudios.Api.Features.Admin;

/// <summary>One page and its uploaded cover (null = the page shows its built-in default).</summary>
public sealed record AdminPageCoverDto(string Page, ImageDto? Image, DateTime? UpdatedAt);

/// <summary>
/// Cover photos of public pages (Packages, Contact, About). Stored in the PUBLIC media store like
/// portfolio images: resized to WebP + JPEG variants with metadata stripped.
/// </summary>
[ApiController]
[Route("api/admin/page-covers")]
[Authorize(Policy = Policies.Admin)]
public sealed class AdminPageCoversController(AppDbContext db, PublicMediaStore store, IOptions<StorageOptions> storage) : ControllerBase
{
    [HttpGet]
    public async Task<IReadOnlyList<AdminPageCoverDto>> List(CancellationToken ct)
    {
        var covers = await db.PageCovers.AsNoTracking().ToDictionaryAsync(x => x.Page, ct);
        return PageCover.Keys.Select(k => covers.TryGetValue(k, out var c) ? ToDto(c) : new AdminPageCoverDto(k, null, null)).ToList();
    }

    [HttpPost("{page}")]
    [RequestSizeLimit(200_000_000)]
    public async Task<ActionResult<AdminPageCoverDto>> Upload(string page, IFormFile file, CancellationToken ct)
    {
        if (!PageCover.Keys.Contains(page)) return this.NotFoundProblem("Unknown page.");
        if (file.Length > storage.Value.MaxImageUploadMb * 1024L * 1024) return this.FieldError("file", "Image is too large.");
        var cover = await db.PageCovers.SingleOrDefaultAsync(x => x.Page == page, ct);
        try
        {
            await using var s = file.OpenReadStream();
            var image = await store.SaveImageAsync(s, PublicMediaStore.CoverWidths, ct);
            var old = cover?.Image.Key;
            if (cover is null) db.PageCovers.Add(cover = new PageCover { Page = page, Image = image });
            else cover.Image = image;
            await db.SaveChangesAsync(ct);
            store.TryDelete(old);
        }
        catch (InvalidUploadException ex) { return this.FieldError("file", ex.Message); }
        return ToDto(cover);
    }

    /// <summary>Remove the uploaded cover; the page goes back to its built-in default.</summary>
    [HttpDelete("{page}")]
    public async Task<IActionResult> Delete(string page, CancellationToken ct)
    {
        var cover = await db.PageCovers.SingleOrDefaultAsync(x => x.Page == page, ct);
        if (cover is null) return this.NotFoundProblem("This page has no uploaded cover.");
        db.PageCovers.Remove(cover);
        await db.SaveChangesAsync(ct);
        store.TryDelete(cover.Image.Key);
        return NoContent();
    }

    private static AdminPageCoverDto ToDto(PageCover c) => new(c.Page, ImageDto.From(c.Image, ""), c.UpdatedAt);
}
