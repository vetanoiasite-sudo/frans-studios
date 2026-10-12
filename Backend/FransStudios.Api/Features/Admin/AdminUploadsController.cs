using System.ComponentModel.DataAnnotations;
using FransStudios.Api.Authentication;
using FransStudios.Api.Data;
using FransStudios.Api.GoogleDrive;
using FransStudios.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FransStudios.Api.Features.Admin;

public sealed record StartUploadRequest(
    [Required, StringLength(255, MinimumLength = 1)] string FileName,
    [Range(1, long.MaxValue)] long Size,
    [StringLength(128)] string? MimeType,
    [Required, StringLength(100, MinimumLength = 1)] string Section);

public sealed record UploadRefRequest([Required, StringLength(4000)] string UploadId);

public sealed record UploadStatusDto(long Received, bool Complete);

public sealed record UploadCompleteDto(string DriveFileId);

/// <summary>
/// Uploading an album's files into the studio's Google Drive from the admin. The browser uploads each file
/// in chunks with Google's resumable protocol: straight to Google (Direct mode) or through
/// <c>uploads/chunk</c> (Proxy mode). Status checks always go through the server.
/// </summary>
[ApiController]
[Route("api/admin/albums/{id:int}")]
[Authorize(Policy = Policies.Admin)]
public sealed class AdminUploadsController(AppDbContext db, DriveUploadService uploads, AuditService audit) : ControllerBase
{
    /// <summary>Creates the album's folder (with Photos and Videos) inside the upload root and assigns it.</summary>
    [HttpPost("drive-folder/create")]
    public async Task<IActionResult> CreateFolder(int id, CancellationToken ct)
    {
        var album = await db.Albums.SingleOrDefaultAsync(a => a.Id == id, ct);
        if (album is null) return this.NotFoundProblem("Album not found.");
        if (album.DriveFolderId is not null) return this.FieldError("folder", "This album already has a Google Drive folder.");

        return await RunAsync(async () =>
        {
            await uploads.CreateAlbumFolderAsync(album, ct);
            audit.Add(AuditActions.AlbumFolderCreated, "Album", id, new { album.DriveFolderId, album.DriveFolderName });
            await db.SaveChangesAsync(ct);
            return Ok(new { album.DriveFolderId, album.DriveFolderName });
        });
    }

    [HttpPost("uploads")]
    public async Task<IActionResult> Start(int id, StartUploadRequest request, CancellationToken ct)
    {
        var album = await db.Albums.AsNoTracking().SingleOrDefaultAsync(a => a.Id == id, ct);
        if (album is null) return this.NotFoundProblem("Album not found.");
        return await RunAsync(async () =>
            Ok(await uploads.StartAsync(album, request.FileName, request.Size, request.MimeType, request.Section, ct)));
    }

    [HttpPost("uploads/status")]
    public Task<IActionResult> Status(int id, UploadRefRequest request, CancellationToken ct) =>
        RunAsync(async () =>
        {
            var p = await uploads.StatusAsync(id, request.UploadId, ct);
            return Ok(new UploadStatusDto(p.Received, p.FileId is not null));
        });

    /// <summary>Proxy mode only: one chunk of the file as the raw request body.</summary>
    [HttpPut("uploads/chunk")]
    [RequestSizeLimit(70_000_000)]
    public Task<IActionResult> Chunk(int id, [FromQuery, Required] string uploadId, [FromQuery] long offset, CancellationToken ct) =>
        RunAsync(async () =>
        {
            if (Request.ContentLength is not > 0) return this.FieldError("body", "Send the chunk bytes as the request body.");
            var p = await uploads.ChunkAsync(id, uploadId, offset, Request.ContentLength.Value, Request.Body, ct);
            return Ok(new UploadStatusDto(p.Received, p.FileId is not null));
        });

    [HttpPost("uploads/complete")]
    public async Task<IActionResult> Complete(int id, UploadRefRequest request, CancellationToken ct)
    {
        var album = await db.Albums.AsNoTracking().SingleOrDefaultAsync(a => a.Id == id, ct);
        if (album is null) return this.NotFoundProblem("Album not found.");
        return await RunAsync(async () =>
        {
            var fileId = await uploads.CompleteAsync(album, request.UploadId, ct);
            await audit.WriteAsync(AuditActions.FileUploaded, "Album", id, new { DriveFileId = fileId }, ct: ct);
            return Ok(new UploadCompleteDto(fileId));
        });
    }

    /// <summary>Turns upload / Drive errors into ProblemDetails the admin can read.</summary>
    private async Task<IActionResult> RunAsync(Func<Task<IActionResult>> action)
    {
        try { return await action(); }
        catch (DriveUploadException ex) { return Problem(statusCode: ex.StatusCode, title: ex.Message); }
        catch (DriveApiException ex) when (ex.Reason is "storageQuotaExceeded" or "quotaExceeded")
        {
            return Problem(statusCode: 507, title: "The studio's Google Drive is full. Free up space or upgrade the Google storage plan.");
        }
        catch (DriveApiException ex) when (ex.StatusCode == 404)
        {
            return Problem(statusCode: 409, title: "The album's Google Drive folder was not found. It may have been deleted or moved.");
        }
        catch (DriveApiException ex)
        {
            return Problem(statusCode: 502, title: $"Google Drive refused the request: {ex.Message}");
        }
    }
}
