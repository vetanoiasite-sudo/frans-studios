using FransStudios.Api.Authentication;
using FransStudios.Api.Configuration;
using FransStudios.Api.Data;
using FransStudios.Api.Entities;
using FransStudios.Api.Features.Admin;
using FransStudios.Api.GoogleDrive;
using FransStudios.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FransStudios.Api.Features.ClientPortal;

public sealed record ClientMe(string Name, int AlbumCount);

public sealed record ClientAlbumCard(Guid Id, string Title, DateOnly? EventDate, Guid? CoverFileId,
    int FileCount, long TotalBytes, int PhotoCount, int VideoCount);

public sealed record ClientAlbumDetail(Guid Id, string Title, string? Description, DateOnly? EventDate, Guid? CoverFileId,
    int FileCount, long TotalBytes, int PhotoCount, int VideoCount, int OtherCount, bool DownloadsEnabled,
    IReadOnlyList<SectionInfo> Sections, bool Syncing);

public sealed record ClientFileDto(Guid Id, string Name, string Section, MediaKind Kind, string MimeType, long SizeBytes,
    int? Width, int? Height, long? DurationMs, bool HasThumbnail);

/// <summary>
/// Everything a client can reach. SECURITY: every query is rooted in <see cref="OwnAlbums"/> /
/// <see cref="OwnFiles"/>, which filter by the client id from the authenticated session.
/// Ids in the URL are only ever used as additional filters, never as a source of identity.
/// Anything not owned (or not published) is reported as 404 so its existence isn't revealed.
/// </summary>
[ApiController]
[Route("api/client")]
[Authorize(Policy = Policies.Client)]
public sealed class ClientPortalController(
    AppDbContext db,
    CurrentUser currentUser,
    AlbumMediaService media,
    AlbumSyncService sync,
    AlbumSyncQueue syncQueue,
    AuditService audit,
    IOptions<GoogleDriveOptions> driveOptions,
    TimeProvider clock,
    JpegConversionService jpeg) : ControllerBase
{
    private IQueryable<Album> OwnAlbums =>
        db.Albums.AsNoTracking().Where(a => a.ClientId == currentUser.ClientId && a.Status == AlbumStatus.Published);

    private IQueryable<AlbumFile> OwnFiles =>
        db.AlbumFiles.AsNoTracking().Where(f => !f.IsRemoved
                                                 && f.Album.ClientId == currentUser.ClientId
                                                 && f.Album.Status == AlbumStatus.Published
                                                 && f.Album.DeletedAt == null);

    [HttpGet("me")]
    public async Task<ClientMe> Me(CancellationToken ct)
    {
        var name = await db.Clients.AsNoTracking().Where(c => c.Id == currentUser.ClientId).Select(c => c.FullName).SingleAsync(ct);
        return new ClientMe(name, await OwnAlbums.CountAsync(ct));
    }

    [HttpGet("albums")]
    public async Task<IReadOnlyList<ClientAlbumCard>> Albums(CancellationToken ct) =>
        await OwnAlbums
            .OrderByDescending(a => a.EventDate ?? DateOnly.MinValue).ThenByDescending(a => a.CreatedAt)
            .Select(a => new ClientAlbumCard(a.PublicId, a.Title, a.EventDate, a.CoverFileId, a.FileCount, a.TotalBytes,
                a.Files.Count(f => !f.IsRemoved && f.Kind == MediaKind.Image),
                a.Files.Count(f => !f.IsRemoved && f.Kind == MediaKind.Video)))
            .ToListAsync(ct);

    [HttpGet("albums/{id:guid}")]
    public async Task<ActionResult<ClientAlbumDetail>> Album(Guid id, CancellationToken ct)
    {
        var album = await OwnAlbums.SingleOrDefaultAsync(a => a.PublicId == id, ct);
        if (album is null) return this.NotFoundProblem("Album not found.");

        // First visit: index synchronously so the client sees files. Afterwards refresh in the background when stale.
        var syncing = false;
        if (album.LastSyncedAt is null)
        {
            await sync.SyncAsync(album.Id, ct);
            album = await OwnAlbums.SingleAsync(a => a.Id == album.Id, ct);
        }
        else if (clock.GetUtcNow().UtcDateTime - album.LastSyncedAt > TimeSpan.FromMinutes(driveOptions.Value.SyncStaleMinutes))
        {
            syncQueue.Enqueue(album.Id);
            syncing = true;
        }

        var counts = await db.AlbumFiles.AsNoTracking().Where(f => f.AlbumId == album.Id && !f.IsRemoved)
            .GroupBy(f => f.Kind).Select(g => new { Kind = g.Key, Count = g.Count() }).ToListAsync(ct);
        int Count(MediaKind k) => counts.FirstOrDefault(c => c.Kind == k)?.Count ?? 0;

        return new ClientAlbumDetail(album.PublicId, album.Title, album.Description, album.EventDate, album.CoverFileId,
            album.FileCount, album.TotalBytes, Count(MediaKind.Image), Count(MediaKind.Video), Count(MediaKind.Other),
            album.DownloadsEnabled, await AdminAlbumsController.SectionsAsync(db, album.Id, ct), syncing);
    }

    [HttpGet("albums/{id:guid}/files")]
    public async Task<ActionResult<Paged<ClientFileDto>>> Files(Guid id, string? section, MediaKind? kind,
        int page = 1, int pageSize = 200, CancellationToken ct = default)
    {
        var albumId = await OwnAlbums.Where(a => a.PublicId == id).Select(a => (int?)a.Id).SingleOrDefaultAsync(ct);
        if (albumId is null) return this.NotFoundProblem("Album not found.");

        var q = OwnFiles.Where(f => f.AlbumId == albumId);
        if (section is not null) q = q.Where(f => f.Section == section || f.Section.StartsWith(section + "/"));
        if (kind is not null) q = q.Where(f => f.Kind == kind);

        (page, pageSize) = ApiHelpers.Clamp(page, pageSize, 500);
        var total = await q.CountAsync(ct);
        var items = await q.OrderBy(f => f.SortOrder).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(f => new ClientFileDto(f.Id, f.Name, f.Section, f.Kind, f.MimeType, f.SizeBytes, f.Width, f.Height, f.DurationMs, f.HasThumbnail))
            .ToListAsync(ct);
        return new Paged<ClientFileDto>(items, total, page, pageSize);
    }

    [HttpGet("files/{id:guid}/thumbnail")]
    public async Task<IResult> Thumbnail(Guid id, ThumbSize size = ThumbSize.Small, CancellationToken ct = default)
    {
        var file = await OwnFiles.SingleOrDefaultAsync(f => f.Id == id, ct);
        if (file is null) return Results.NotFound();
        var result = await media.ThumbnailAsync(file, size, ct);
        Response.Headers.CacheControl = "private, max-age=86400";
        return result;
    }

    /// <summary>
    /// Streams the original file. mode=stream plays inline (images/videos), mode=download forces a save dialog.
    /// Supports HTTP Range for seeking and resumable downloads. format=jpg (downloads of HEIC/HEIF photos only)
    /// sends a full-size JPEG copy instead of the original.
    /// </summary>
    [HttpGet("files/{id:guid}/content")]
    public async Task Content(Guid id, string mode = "stream", string? format = null, CancellationToken ct = default)
    {
        var row = await OwnFiles.Where(f => f.Id == id).Select(f => new { File = f, f.Album.DownloadsEnabled }).SingleOrDefaultAsync(ct);
        if (row is null) { await Results.Problem(statusCode: 404, title: "File not found.").ExecuteAsync(HttpContext); return; }

        var download = mode == "download";
        if (download && !row.DownloadsEnabled)
        {
            await Results.Problem(statusCode: 403, title: "Downloads are not enabled for this album.").ExecuteAsync(HttpContext);
            return;
        }
        if (!download && row.File.Kind == MediaKind.Other)
        {
            await Results.Problem(statusCode: 400, title: "This file type can't be previewed.").ExecuteAsync(HttpContext);
            return;
        }

        var asJpeg = download && format == "jpg" && JpegConversionService.CanConvert(row.File.MimeType, row.File.Name);
        if (download && AlbumMediaService.IsFromStart(Request.Headers.Range))
            await audit.WriteAsync(AuditActions.FileDownloaded, "AlbumFile", row.File.Id, new { row.File.Name, row.File.SizeBytes, AsJpeg = asJpeg }, ct: ct);

        if (asJpeg)
            await jpeg.WriteJpegAsync(HttpContext, row.File.DriveFileId, row.File.Id.ToString("N"), row.File.Name, row.File.SizeBytes, row.File.DriveModifiedAt, ct);
        else
            await media.StreamAsync(HttpContext, row.File, download, ct);
    }

    /// <summary>Streams a ZIP of one top-level section (e.g. "Photos"). Use section="" for files in the album root.</summary>
    [HttpGet("albums/{id:guid}/zip")]
    public async Task Zip(Guid id, string section = "", CancellationToken ct = default)
    {
        var album = await OwnAlbums.Where(a => a.PublicId == id).Select(a => new { a.Id, a.Title, a.DownloadsEnabled }).SingleOrDefaultAsync(ct);
        if (album is null) { await Results.Problem(statusCode: 404, title: "Album not found.").ExecuteAsync(HttpContext); return; }
        if (!album.DownloadsEnabled)
        {
            await Results.Problem(statusCode: 403, title: "Downloads are not enabled for this album.").ExecuteAsync(HttpContext);
            return;
        }

        var q = OwnFiles.Where(f => f.AlbumId == album.Id);
        q = section.Length == 0 ? q.Where(f => f.Section == "") : q.Where(f => f.Section == section || f.Section.StartsWith(section + "/"));
        var files = await q.OrderBy(f => f.SortOrder).ToListAsync(ct);
        if (files.Count == 0) { await Results.Problem(statusCode: 404, title: "Nothing to download.").ExecuteAsync(HttpContext); return; }

        var limit = driveOptions.Value.MaxSectionZipGb * 1024L * 1024 * 1024;
        if (files.Sum(f => f.SizeBytes) > limit)
        {
            await Results.Problem(statusCode: 413, title: "This section is too large to download as one ZIP. Please download the files individually.")
                .ExecuteAsync(HttpContext);
            return;
        }

        await audit.WriteAsync(AuditActions.SectionDownloaded, "Album", album.Id, new { section, files.Count }, ct: ct);
        var zipName = $"{album.Title}{(section.Length > 0 ? " - " + section : "")}.zip";
        await media.StreamZipAsync(HttpContext, zipName, files, section, ct);
    }
}
