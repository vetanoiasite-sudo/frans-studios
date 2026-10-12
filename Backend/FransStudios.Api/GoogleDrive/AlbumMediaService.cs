using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using FransStudios.Api.Configuration;
using FransStudios.Api.Data;
using FransStudios.Api.Entities;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using RangeHeaderValue = System.Net.Http.Headers.RangeHeaderValue;

namespace FransStudios.Api.GoogleDrive;

public enum ThumbSize { Small, Medium, Large }

/// <summary>
/// Delivers PRIVATE client media from Google Drive. Callers must have authorized access to the
/// <see cref="AlbumFile"/> before calling (ownership check happens in the controller query).
///
/// Bytes are streamed through this server with a fixed ~80 KB buffer: memory use is independent of
/// file size, HTTP Range is forwarded so seeking and resumed downloads work for multi-GB files.
/// </summary>
public sealed class AlbumMediaService(
    AppDbContext db,
    DriveClient drive,
    DriveTokenService tokens,
    AlbumSyncQueue syncQueue,
    IOptions<StorageOptions> storage,
    ILogger<AlbumMediaService> logger)
{
    private const int CopyBufferSize = 81920;

    // Formats that are safe to render inline on our origin. Everything else (SVG, HTML, PDF, …) is forced to download.
    private static readonly HashSet<string> InlineSafeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/png", "image/webp", "image/gif", "image/avif",
        "video/mp4", "video/webm", "video/quicktime", "audio/mpeg", "audio/mp4",
    };

    public static int Pixels(ThumbSize size) => size switch { ThumbSize.Small => 480, ThumbSize.Medium => 1200, _ => 2048 };

    // ── Thumbnails (cached on disk, outside any web root) ───────────────────

    public async Task<IResult> ThumbnailAsync(AlbumFile file, ThumbSize size, CancellationToken ct) =>
        await ThumbnailCoreAsync(await DriveForAlbumAsync(file.AlbumId, ct), file.DriveFileId, file.Id.ToString("N"), file.DriveModifiedAt, size,
            () => syncQueue.Enqueue(file.AlbumId), ct);

    /// <summary>
    /// A Drive client for the account whose Drive holds the album: the connected one, or a previously connected
    /// one until the album is moved (see <see cref="DriveTokenService.AccountIdForAsync"/>).
    /// </summary>
    private async Task<DriveClient> DriveForAlbumAsync(int albumId, CancellationToken ct) =>
        drive.ForAccount(await tokens.AccountIdForAsync(
            await db.Albums.AsNoTracking().Where(a => a.Id == albumId).Select(a => a.DriveAccountEmail).SingleOrDefaultAsync(ct), ct));

    /// <summary>Thumbnail of any Drive file (admin Drive manager). <paramref name="modified"/> versions the cache.</summary>
    public Task<IResult> ThumbnailAsync(string driveFileId, DateTime? modified, ThumbSize size, CancellationToken ct) =>
        ThumbnailCoreAsync(drive, driveFileId, "d" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(driveFileId)))[..31].ToLowerInvariant(),
            modified, size, null, ct);

    private async Task<IResult> ThumbnailCoreAsync(DriveClient albumDrive, string driveFileId, string cacheId, DateTime? modified, ThumbSize size,
        Action? onGone, CancellationToken ct)
    {
        var px = Pixels(size);
        var version = modified?.Ticks ?? 0;
        var dir = Path.Combine(storage.Value.PrivateCachePath, "thumbs", cacheId[..2]);
        var path = Path.Combine(dir, $"{cacheId}-{px}-{version}.bin");

        byte[]? bytes = File.Exists(path) ? await File.ReadAllBytesAsync(path, ct) : null;
        if (bytes is null)
        {
            try
            {
                bytes = await albumDrive.GetThumbnailAsync(driveFileId, px, ct);
            }
            catch (DriveApiException ex) when (ex.StatusCode == 404)
            {
                onGone?.Invoke();
                bytes = null;
            }
            if (bytes is null) return Results.NotFound();

            Directory.CreateDirectory(dir);
            var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await File.WriteAllBytesAsync(tmp, bytes, ct);
            File.Move(tmp, path, overwrite: true);
        }

        return Results.Bytes(bytes, SniffImageType(bytes), lastModified: modified,
            entityTag: new EntityTagHeaderValue($"\"{cacheId}-{px}-{version}\""));
    }

    // ── Original bytes: streaming proxy with Range support ──────────────────

    public async Task StreamAsync(HttpContext http, AlbumFile file, bool asDownload, CancellationToken ct) =>
        await StreamCoreAsync(await DriveForAlbumAsync(file.AlbumId, ct), http, file.DriveFileId, file.Name, file.MimeType, file.SizeBytes,
            file.DriveModifiedAt, asDownload, () => syncQueue.Enqueue(file.AlbumId), ct);

    /// <summary>Streams any Drive file's bytes (Range-aware). <paramref name="onGone"/> runs if Drive says it no longer exists.</summary>
    public Task StreamAsync(HttpContext http, string driveFileId, string name, string mimeType, long size, DateTime? modified,
        bool asDownload, Action? onGone, CancellationToken ct) =>
        StreamCoreAsync(drive, http, driveFileId, name, mimeType, size, modified, asDownload, onGone, ct);

    private async Task StreamCoreAsync(DriveClient albumDrive, HttpContext http, string driveFileId, string name, string mimeType, long size,
        DateTime? modified, bool asDownload, Action? onGone, CancellationToken ct)
    {
        var request = http.Request;
        var response = http.Response;

        var range = ParseSingleRange(request.Headers.Range.ToString());
        HttpResponseMessage upstream;
        try
        {
            upstream = await albumDrive.OpenMediaAsync(driveFileId, range, ct);
        }
        catch (DriveNotConnectedException)
        {
            await WriteProblemAsync(http, 503, "Files are temporarily unavailable. Please try again later.");
            return;
        }

        using (upstream)
        {
            if (upstream.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
            {
                response.StatusCode = 416;
                response.Headers.ContentRange = $"bytes */{size}";
                return;
            }
            if (upstream.StatusCode is not (HttpStatusCode.OK or HttpStatusCode.PartialContent))
            {
                var ex = await DriveClient.ToExceptionAsync(upstream, ct);
                logger.LogWarning("Drive media {Status}/{Reason} for Drive file {DriveFileId}", ex.StatusCode, ex.Reason, driveFileId);
                if (ex.StatusCode == 404)
                {
                    onGone?.Invoke();
                    await WriteProblemAsync(http, 404, "This file is no longer available.");
                }
                else if (ex.Reason is "downloadQuotaExceeded" or "userRateLimitExceeded" or "rateLimitExceeded" || ex.StatusCode == 429)
                {
                    response.Headers.RetryAfter = "600";
                    await WriteProblemAsync(http, 503, "This file is very popular right now. Please try again in a few minutes.");
                }
                else
                {
                    await WriteProblemAsync(http, 502, "The file could not be retrieved. Please try again.");
                }
                return;
            }

            var inline = !asDownload && InlineSafeTypes.Contains(mimeType);
            response.StatusCode = (int)upstream.StatusCode;
            response.ContentType = inline ? mimeType : "application/octet-stream";
            response.ContentLength = upstream.Content.Headers.ContentLength;
            if (upstream.Content.Headers.ContentRange is { } cr) response.Headers.ContentRange = cr.ToString();
            response.Headers.AcceptRanges = "bytes";
            if (upstream.Headers.ETag is { } etag) response.Headers.ETag = etag.ToString();
            if (modified is { } lastModified) response.GetTypedHeaders().LastModified = lastModified;
            response.Headers.CacheControl = "private, max-age=0, must-revalidate";
            response.Headers.ContentDisposition = new ContentDispositionHeaderValue(inline ? "inline" : "attachment")
            {
                FileNameStar = name,
                FileName = AsciiFileName(name),
            }.ToString();
            // Even if a browser decided to render the bytes, it gets no script or network capability.
            response.Headers.ContentSecurityPolicy = "default-src 'none'; img-src 'self'; media-src 'self'; style-src 'unsafe-inline'; sandbox";

            DisableBuffering(http);
            await using var source = await upstream.Content.ReadAsStreamAsync(ct);
            try
            {
                await source.CopyToAsync(response.Body, CopyBufferSize, ct);
            }
            catch (Exception e) when (e is OperationCanceledException or IOException && http.RequestAborted.IsCancellationRequested)
            {
                // Client navigated away / paused the video. Normal for streaming; nothing to do.
            }
        }
    }

    /// <summary>Streams a ZIP (stored, not compressed — photos/videos don't compress) of the given files.</summary>
    public async Task StreamZipAsync(HttpContext http, string zipName, IReadOnlyList<AlbumFile> files, string stripPrefix, CancellationToken ct)
    {
        var response = http.Response;
        response.ContentType = "application/zip";
        response.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment")
        {
            FileNameStar = zipName,
            FileName = AsciiFileName(zipName),
        }.ToString();
        response.Headers.CacheControl = "private, no-store";
        DisableBuffering(http);
        // ZipArchive writes each entry's few-byte data descriptor synchronously even via the async API.
        // File contents are still copied asynchronously; only those small trailers block, so allow it here only.
        if (http.Features.Get<IHttpBodyControlFeature>() is { } bodyControl) bodyControl.AllowSynchronousIO = true;

        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var albumDrive = files.Count > 0 ? await DriveForAlbumAsync(files[0].AlbumId, ct) : drive; // a ZIP is always one album's files
        try
        {
            await using var zip = await ZipArchive.CreateAsync(response.Body, ZipArchiveMode.Create, leaveOpen: true, entryNameEncoding: null, ct);
            foreach (var file in files)
            {
                var relative = file.Section.Length > stripPrefix.Length ? file.Section[stripPrefix.Length..].TrimStart('/') : "";
                var entryName = UniqueName(usedNames, relative.Length == 0 ? file.Name : $"{relative}/{file.Name}");

                using var upstream = await albumDrive.OpenMediaAsync(file.DriveFileId, null, ct);
                if (!upstream.IsSuccessStatusCode)
                {
                    logger.LogWarning("Skipping {FileId} in ZIP: Drive returned {Status}", file.Id, (int)upstream.StatusCode);
                    continue;
                }
                var entry = zip.CreateEntry(entryName, CompressionLevel.NoCompression);
                if (file.DriveModifiedAt is { } m) entry.LastWriteTime = m;
                await using var target = await entry.OpenAsync(ct);
                await using var source = await upstream.Content.ReadAsStreamAsync(ct);
                await source.CopyToAsync(target, CopyBufferSize, ct);
            }
        }
        catch (Exception e) when (e is OperationCanceledException or IOException && http.RequestAborted.IsCancellationRequested)
        {
        }
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    /// <summary>Accepts exactly one "bytes=start-end" range; anything else means "whole file".</summary>
    public static RangeHeaderValue? ParseSingleRange(string? header)
    {
        if (string.IsNullOrWhiteSpace(header)) return null;
        if (!RangeHeaderValue.TryParse(header, out var parsed)) return null;
        if (!string.Equals(parsed.Unit, "bytes", StringComparison.OrdinalIgnoreCase) || parsed.Ranges.Count != 1) return null;
        var r = parsed.Ranges.First();
        if (r.From is null && r.To is null) return null;
        if (r.From is not null && r.To is not null && r.From > r.To) return null;
        return new RangeHeaderValue(r.From, r.To);
    }

    public static bool IsFromStart(string? rangeHeader)
    {
        var r = ParseSingleRange(rangeHeader);
        return r is null || r.Ranges.First().From is 0;
    }

    private static void DisableBuffering(HttpContext http)
    {
        http.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
        // Kestrel would otherwise drop very slow mobile connections on multi-GB downloads.
        if (http.Features.Get<IHttpMinResponseDataRateFeature>() is { } rate) rate.MinDataRate = null;
        http.Response.Headers["X-Accel-Buffering"] = "no";
    }

    private static Task WriteProblemAsync(HttpContext http, int status, string title) =>
        Results.Problem(statusCode: status, title: title).ExecuteAsync(http);

    private static string AsciiFileName(string name)
    {
        var chars = name.Select(c => c is >= ' ' and <= '~' and not '"' and not '\\' ? c : '_').ToArray();
        return new string(chars);
    }

    private static string UniqueName(HashSet<string> used, string name)
    {
        if (used.Add(name)) return name;
        var ext = Path.GetExtension(name);
        var stem = name[..^ext.Length];
        for (var i = 2; ; i++)
        {
            var candidate = $"{stem} ({i}){ext}";
            if (used.Add(candidate)) return candidate;
        }
    }

    private static string SniffImageType(byte[] b) => b switch
    {
        [0xFF, 0xD8, ..] => "image/jpeg",
        [0x89, 0x50, 0x4E, 0x47, ..] => "image/png",
        [0x52, 0x49, 0x46, 0x46, _, _, _, _, 0x57, 0x45, 0x42, 0x50, ..] => "image/webp",
        [0x47, 0x49, 0x46, ..] => "image/gif",
        _ => "application/octet-stream",
    };
}
