using System.ComponentModel.DataAnnotations;
using FransStudios.Api.Authentication;
using FransStudios.Api.Data;
using FransStudios.Api.Entities;
using FransStudios.Api.GoogleDrive;
using FransStudios.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FransStudios.Api.Features.Admin;

/// <summary>The client album that uses a Drive folder.</summary>
public sealed record DriveAlbumDto(int Id, string Title, int ClientId, string ClientName, AlbumStatus Status, int FileCount);

public sealed record DriveEntryDto(
    string Id, string Name, string MimeType, bool IsFolder, bool IsGoogleDoc, long? Size, DateTime? ModifiedTime,
    bool HasThumbnail, string? WebViewLink, string? ShortcutTargetId, bool ShortcutToFolder,
    int? Width, int? Height, long? DurationMs, bool CanRename, bool CanTrash, bool CanMove, bool CanAddChildren,
    DriveAlbumDto? Album = null)
{
    public static DriveEntryDto From(DriveEntry e) => new(
        e.Id, e.Name, e.MimeType, e.MimeType == DriveHttp.FolderMime,
        e.MimeType.StartsWith("application/vnd.google-apps.", StringComparison.Ordinal) && e.MimeType != DriveHttp.FolderMime,
        e.Size, e.ModifiedTime, e.HasThumbnail, e.WebViewLink, e.ShortcutTargetId, e.ShortcutTargetMimeType == DriveHttp.FolderMime,
        e.Width, e.Height, e.DurationMs, e.CanRename, e.CanTrash, e.CanMove, e.CanAddChildren);
}

/// <param name="FolderAlbum">The album the listed folder belongs to (it is the album folder, or one of its
/// sections such as Photos), so the page can offer "Set as album cover" on its files.</param>
public sealed record DriveListDto(IReadOnlyList<DriveEntryDto> Items, string? NextPageToken, DriveAlbumDto? FolderAlbum = null);

public sealed record AlbumCoverResultDto(int AlbumId, string AlbumTitle, string ClientName);

public sealed record DriveCrumb(string Id, string Name);

public sealed record CreateFolderRequest([Required, StringLength(255, MinimumLength = 1)] string Name, [Required, StringLength(128)] string ParentId);

public sealed record RenameRequest([Required, StringLength(255, MinimumLength = 1)] string Name);

public sealed record MoveRequest([Required, MinLength(1), MaxLength(500)] string[] Ids, [Required, StringLength(128)] string ToFolderId);

public sealed record TrashRequest([Required, MinLength(1), MaxLength(500)] string[] Ids);

public sealed record FolderUploadRequest(
    [Required, StringLength(128)] string ParentId,
    [Required, StringLength(255, MinimumLength = 1)] string FileName,
    [Range(1, long.MaxValue)] long Size,
    [StringLength(128)] string? MimeType);

/// <summary>Result of a bulk action: which items worked and which didn't (with Google's reason).</summary>
public sealed record BulkResult(IReadOnlyList<string> Done, IReadOnlyList<BulkFailure> Failed);

public sealed record BulkFailure(string Id, string Error);

/// <summary>
/// The admin's window onto the studio's whole Google Drive: browse, search, preview, download, create folders,
/// upload, rename, move and send to trash (never a permanent delete). Admins only; Google's own permission
/// flags (canRename, canTrash, …) are passed through so the page only offers what Drive allows.
/// Changes inside an album's folder re-sync that album.
/// </summary>
[ApiController]
[Route("api/admin/drive")]
[Authorize(Policy = Policies.Admin)]
public sealed class AdminDriveFilesController(
    AppDbContext db, DriveClient drive, DriveUploadService uploads, AlbumMediaService media, AlbumSyncService sync,
    JpegConversionService jpeg, AuditService audit)
    : ControllerBase
{
    private IQueryable<DriveAlbumDto> AlbumsUsing(IEnumerable<string> folderIds)
    {
        var ids = folderIds.ToList();
        return db.Albums.AsNoTracking().Where(a => a.DriveFolderId != null && ids.Contains(a.DriveFolderId))
            .Select(a => new DriveAlbumDto(a.Id, a.Title, a.ClientId, a.Client.FullName, a.Status, a.FileCount));
    }

    /// <summary>The album whose folder is <paramref name="folderId"/> or its parent (album folder → Photos / Videos).</summary>
    private async Task<DriveAlbumDto?> AlbumOfFolderAsync(string folderId, CancellationToken ct)
    {
        if (folderId == "root") return null;
        var direct = await AlbumsUsing([folderId]).FirstOrDefaultAsync(ct);
        if (direct is not null) return direct;
        var parents = (await drive.GetEntryAsync(folderId, ct)).Parents;
        return parents.Length == 0 ? null : await AlbumsUsing(parents).FirstOrDefaultAsync(ct);
    }

    /// <summary>Marks the folders that are client albums (folder → album, client, status).</summary>
    private async Task<List<DriveEntryDto>> WithAlbumsAsync(IEnumerable<DriveEntry> entries, CancellationToken ct)
    {
        var dtos = entries.Select(DriveEntryDto.From).ToList();
        var folderIds = dtos.Where(d => d.IsFolder).Select(d => d.Id).ToList();
        if (folderIds.Count == 0) return dtos;
        var albums = await db.Albums.AsNoTracking().Where(a => a.DriveFolderId != null && folderIds.Contains(a.DriveFolderId))
            .Select(a => new { a.DriveFolderId, Dto = new DriveAlbumDto(a.Id, a.Title, a.ClientId, a.Client.FullName, a.Status, a.FileCount) })
            .ToListAsync(ct);
        var byFolder = albums.GroupBy(a => a.DriveFolderId!).ToDictionary(g => g.Key, g => g.First().Dto);
        return dtos.Select(d => byFolder.TryGetValue(d.Id, out var album) ? d with { Album = album } : d).ToList();
    }

    [HttpGet("storage")]
    public Task<IActionResult> Storage(CancellationToken ct) => RunAsync(async () => Ok(await drive.GetStorageAsync(ct)));

    /// <summary>A folder's contents (parentId, default My Drive), "shared with me" (view=shared), or a name search (q).</summary>
    [HttpGet("items")]
    public Task<IActionResult> Items(string? parentId, string? q, string? view, string? pageToken, CancellationToken ct) =>
        RunAsync(async () =>
        {
            if (parentId is not null && !DriveClient.IsValidId(parentId)) return this.FieldError("parentId", "Invalid folder id.");
            if (q is { Length: > 100 }) return this.FieldError("q", "Search is too long.");
            if (pageToken is { Length: > 2000 }) return this.FieldError("pageToken", "Invalid page.");
            var (items, next) = await drive.ListEntriesAsync(parentId, q, view == "shared", pageToken, ct);
            var inFolder = string.IsNullOrWhiteSpace(q) && view != "shared";
            var folderAlbum = inFolder && pageToken is null ? await AlbumOfFolderAsync(parentId ?? "root", ct) : null;
            return Ok(new DriveListDto(await WithAlbumsAsync(items, ct), next, folderAlbum));
        });

    [HttpGet("items/{id}")]
    public Task<IActionResult> Item(string id, CancellationToken ct) =>
        RunAsync(async () => DriveClient.IsValidId(id)
            ? Ok((await WithAlbumsAsync([await drive.GetEntryAsync(id, ct)], ct))[0])
            : this.NotFoundProblem("Not found."));

    /// <summary>Breadcrumb from My Drive down to this folder.</summary>
    [HttpGet("items/{id}/trail")]
    public Task<IActionResult> Trail(string id, CancellationToken ct) =>
        RunAsync(async () => DriveClient.IsValidId(id)
            ? Ok((await drive.GetTrailAsync(id, ct)).Select(x => new DriveCrumb(x.Id, x.Name)).ToList())
            : this.NotFoundProblem("Not found."));

    /// <summary><paramref name="v"/> (the item's modified time) only versions the thumbnail cache.</summary>
    [HttpGet("items/{id}/thumbnail")]
    public async Task<IResult> Thumbnail(string id, ThumbSize size = ThumbSize.Small, long v = 0, CancellationToken ct = default)
    {
        if (!DriveClient.IsValidId(id)) return Results.NotFound();
        try { return await media.ThumbnailAsync(id, v > 0 ? new DateTime(v, DateTimeKind.Utc) : null, size, ct); }
        catch (DriveNotConnectedException) { return Results.StatusCode(503); }
    }

    /// <summary>Streams a file for preview (inline for safe image/video types) or as a download; format=jpg converts HEIC/HEIF photos.</summary>
    [HttpGet("items/{id}/content")]
    public async Task Content(string id, bool download = false, string? format = null, CancellationToken ct = default)
    {
        if (!DriveClient.IsValidId(id)) { await Results.NotFound().ExecuteAsync(HttpContext); return; }
        DriveEntry e;
        try { e = await drive.GetEntryAsync(id, ct); }
        catch (DriveApiException ex) when (ex.StatusCode == 404) { await Results.NotFound().ExecuteAsync(HttpContext); return; }
        if (e.MimeType.StartsWith("application/vnd.google-apps.", StringComparison.Ordinal))
        {
            await Results.Problem(statusCode: 400, title: "Google Docs, Sheets and folders open in Google Drive itself.").ExecuteAsync(HttpContext);
            return;
        }
        if (format == "jpg" && JpegConversionService.CanConvert(e.MimeType, e.Name))
        {
            var cacheId = "d" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(e.Id)))[..31].ToLowerInvariant();
            await jpeg.WriteJpegAsync(HttpContext, e.Id, cacheId, e.Name, e.Size ?? 0, e.ModifiedTime, ct);
            return;
        }
        await media.StreamAsync(HttpContext, e.Id, e.Name, e.MimeType, e.Size ?? 0, e.ModifiedTime, download, null, ct);
    }

    /// <summary>
    /// Makes this Drive photo (or video preview frame) the cover of the album it belongs to. A file uploaded a
    /// moment ago may not be indexed yet, so the album is synced first if needed.
    /// </summary>
    [HttpPut("items/{id}/album-cover")]
    public Task<IActionResult> SetAlbumCover(string id, CancellationToken ct) =>
        RunAsync(async () =>
        {
            if (!DriveClient.IsValidId(id)) return this.NotFoundProblem("Not found.");
            var file = await db.AlbumFiles.AsNoTracking().Where(f => f.DriveFileId == id && !f.IsRemoved).FirstOrDefaultAsync(ct);
            if (file is null)
            {
                var entry = await drive.GetEntryAsync(id, ct);
                var parents = new HashSet<string>(entry.Parents);
                foreach (var p in entry.Parents) parents.UnionWith((await drive.GetEntryAsync(p, ct)).Parents);
                foreach (var album in await AlbumsUsing(parents).ToListAsync(ct)) await sync.SyncAsync(album.Id, ct);
                file = await db.AlbumFiles.AsNoTracking().Where(f => f.DriveFileId == id && !f.IsRemoved).FirstOrDefaultAsync(ct);
            }
            if (file is null) return this.FieldError("id", "This file isn't in a client album.");
            if (!(file.Kind == MediaKind.Image || (file.Kind == MediaKind.Video && file.HasThumbnail)))
                return this.FieldError("id", "Only a photo or a video can be the album cover.");

            var target = await db.Albums.Include(a => a.Client).SingleAsync(a => a.Id == file.AlbumId, ct);
            target.CoverFileId = file.Id;
            audit.Add(AuditActions.AlbumUpdated, "Album", target.Id, new { Cover = file.Name });
            await db.SaveChangesAsync(ct);
            return Ok(new AlbumCoverResultDto(target.Id, target.Title, target.Client.FullName));
        });

    [HttpPost("folders")]
    public Task<IActionResult> CreateFolder(CreateFolderRequest request, CancellationToken ct) =>
        RunAsync(async () =>
        {
            await uploads.EnsureCanWriteAsync(ct);
            if (!DriveClient.IsValidId(request.ParentId)) return this.FieldError("parentId", "Invalid folder id.");
            var name = request.Name.Trim();
            if (name.Any(char.IsControl)) return this.FieldError("name", "The name contains invalid characters.");
            var folder = await drive.CreateFolderAsync(request.ParentId, name, ct);
            await audit.WriteAsync(AuditActions.DriveFolderCreated, "DriveItem", folder.Id, new { folder.Name, request.ParentId }, ct: ct);
            await uploads.EnqueueAlbumsAroundAsync([request.ParentId], ct);
            return Ok(DriveEntryDto.From(await drive.GetEntryAsync(folder.Id, ct)));
        });

    [HttpPatch("items/{id}")]
    public Task<IActionResult> Rename(string id, RenameRequest request, CancellationToken ct) =>
        RunAsync(async () =>
        {
            await uploads.EnsureCanWriteAsync(ct);
            if (!DriveClient.IsValidId(id) || id == "root") return this.NotFoundProblem("Not found.");
            var name = request.Name.Trim();
            if (name.Any(char.IsControl)) return this.FieldError("name", "The name contains invalid characters.");
            var before = await drive.GetEntryAsync(id, ct);
            var after = await drive.RenameAsync(id, name, ct);
            await audit.WriteAsync(AuditActions.DriveItemRenamed, "DriveItem", id, new { From = before.Name, To = after.Name }, ct: ct);
            await uploads.EnqueueAlbumsAroundAsync([.. before.Parents, id], ct);
            return Ok(DriveEntryDto.From(after));
        });

    [HttpPost("move")]
    public Task<IActionResult> Move(MoveRequest request, CancellationToken ct) =>
        RunAsync(async () =>
        {
            await uploads.EnsureCanWriteAsync(ct);
            if (!DriveClient.IsValidId(request.ToFolderId)) return this.FieldError("toFolderId", "Invalid folder id.");
            var touched = new HashSet<string> { request.ToFolderId };
            var result = await BulkAsync(request.Ids, async id =>
            {
                if (id == request.ToFolderId) throw new DriveUploadException("A folder can't be moved into itself.");
                var before = await drive.GetEntryAsync(id, ct);
                await drive.MoveAsync(id, request.ToFolderId, before.Parents, ct);
                touched.UnionWith(before.Parents);
            });
            await audit.WriteAsync(AuditActions.DriveItemsMoved, "DriveItem", null, new { request.ToFolderId, result.Done }, ct: ct);
            await uploads.EnqueueAlbumsAroundAsync(touched, ct);
            return Ok(result);
        });

    /// <summary>Sends items to the Drive trash. They can be restored from Google Drive for 30 days.</summary>
    [HttpPost("trash")]
    public Task<IActionResult> Trash(TrashRequest request, CancellationToken ct) =>
        RunAsync(async () =>
        {
            await uploads.EnsureCanWriteAsync(ct);
            var touched = new HashSet<string>();
            var result = await BulkAsync(request.Ids, async id =>
            {
                if (id == "root") throw new DriveUploadException("My Drive itself can't be trashed.");
                var before = await drive.GetEntryAsync(id, ct);
                await drive.TrashAsync(id, ct);
                touched.UnionWith(before.Parents);
                touched.Add(id);
            });
            await audit.WriteAsync(AuditActions.DriveItemsTrashed, "DriveItem", null, new { result.Done }, ct: ct);
            await uploads.EnqueueAlbumsAroundAsync(touched, ct);
            return Ok(result);
        });

    // ── Uploads (same resumable protocol as album uploads, into any folder) ──

    [HttpPost("uploads")]
    public Task<IActionResult> StartUpload(FolderUploadRequest request, CancellationToken ct) =>
        RunAsync(async () => Ok(await uploads.StartInFolderAsync(request.ParentId, request.FileName, request.Size, request.MimeType, ct)));

    [HttpPost("uploads/status")]
    public Task<IActionResult> UploadStatus(UploadRefRequest request, CancellationToken ct) =>
        RunAsync(async () =>
        {
            var p = await uploads.StatusAsync(DriveUploadService.AnyFolderScope, request.UploadId, ct);
            return Ok(new UploadStatusDto(p.Received, p.FileId is not null));
        });

    [HttpPut("uploads/chunk")]
    [RequestSizeLimit(70_000_000)]
    public Task<IActionResult> UploadChunk([FromQuery, Required] string uploadId, [FromQuery] long offset, CancellationToken ct) =>
        RunAsync(async () =>
        {
            if (Request.ContentLength is not > 0) return this.FieldError("body", "Send the chunk bytes as the request body.");
            var p = await uploads.ChunkAsync(DriveUploadService.AnyFolderScope, uploadId, offset, Request.ContentLength.Value, Request.Body, ct);
            return Ok(new UploadStatusDto(p.Received, p.FileId is not null));
        });

    [HttpPost("uploads/complete")]
    public Task<IActionResult> CompleteUpload(UploadRefRequest request, CancellationToken ct) =>
        RunAsync(async () =>
        {
            var fileId = await uploads.CompleteInFolderAsync(request.UploadId, ct);
            await audit.WriteAsync(AuditActions.FileUploaded, "DriveItem", fileId, ct: ct);
            return Ok(new UploadCompleteDto(fileId));
        });

    // ── helpers ─────────────────────────────────────────────────────────────

    private static async Task<BulkResult> BulkAsync(IEnumerable<string> ids, Func<string, Task> action)
    {
        var done = new List<string>();
        var failed = new List<BulkFailure>();
        foreach (var id in ids.Distinct())
        {
            if (!DriveClient.IsValidId(id)) { failed.Add(new BulkFailure(id, "Invalid id.")); continue; }
            try { await action(id); done.Add(id); }
            catch (DriveUploadException ex) { failed.Add(new BulkFailure(id, ex.Message)); }
            catch (DriveApiException ex) { failed.Add(new BulkFailure(id, ex.StatusCode == 404 ? "Not found." : ex.Message)); }
        }
        return new BulkResult(done, failed);
    }

    private async Task<IActionResult> RunAsync(Func<Task<IActionResult>> action)
    {
        try { return await action(); }
        catch (DriveUploadException ex) { return Problem(statusCode: ex.StatusCode, title: ex.Message); }
        catch (DriveNotConnectedException ex) { return Problem(statusCode: 503, title: ex.Message); }
        catch (DriveApiException ex) when (ex.Reason is "storageQuotaExceeded" or "quotaExceeded")
        {
            return Problem(statusCode: 507, title: "The studio's Google Drive is full. Free up space or upgrade the Google storage plan.");
        }
        catch (DriveApiException ex) when (ex.StatusCode == 404) { return this.NotFoundProblem("That file or folder was not found in Drive."); }
        catch (DriveApiException ex) when (ex.StatusCode == 403)
        {
            return Problem(statusCode: 403, title: $"Google Drive doesn't allow that: {ex.Message}");
        }
        catch (DriveApiException ex) { return Problem(statusCode: 502, title: $"Google Drive refused the request: {ex.Message}"); }
    }
}
