using System.Net;
using FransStudios.Api.Configuration;
using ImageMagick;
using Microsoft.Extensions.Options;

namespace FransStudios.Api.GoogleDrive;

/// <summary>
/// "Download as JPG" for HEIC/HEIF photos (iPhone's default format, which many Windows and Android devices
/// can't open). The original in Drive is never changed: it is read once, converted at full size, and the JPEG
/// is cached on disk (outside any web root) per file version, so later downloads are instant.
/// </summary>
public sealed class JpegConversionService(DriveClient drive, IOptions<StorageOptions> storage, ILogger<JpegConversionService> logger)
{
    /// <summary>Larger originals aren't converted (a HEIC photo is normally 1–10 MB).</summary>
    private const long MaxSourceBytes = 150L * 1024 * 1024;

    // Decoding a 48-megapixel photo takes a few hundred MB of memory: limit how many run at once.
    private static readonly SemaphoreSlim Gate = new(2, 2);

    private static readonly HashSet<string> HeifTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/heic", "image/heif", "image/heic-sequence", "image/heif-sequence",
    };

    public static bool CanConvert(string mimeType, string name) =>
        HeifTypes.Contains(mimeType)
        || name.EndsWith(".heic", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".heif", StringComparison.OrdinalIgnoreCase);

    /// <summary>"IMG_0042.HEIC" → "IMG_0042.jpg".</summary>
    public static string JpegName(string name) => Path.GetFileNameWithoutExtension(name) + ".jpg";

    /// <summary>
    /// Sends the file as a JPEG download. <paramref name="cacheId"/> identifies the file (hex/alphanumeric);
    /// <paramref name="modified"/> versions the cached conversion.
    /// </summary>
    public async Task WriteJpegAsync(HttpContext http, string driveFileId, string cacheId, string name, long size, DateTime? modified, CancellationToken ct)
    {
        if (size > MaxSourceBytes)
        {
            await Results.Problem(statusCode: 413, title: "This photo is too large to convert. Download the original instead.").ExecuteAsync(http);
            return;
        }

        string path;
        try { path = await GetOrCreateAsync(driveFileId, cacheId, modified, ct); }
        catch (MagickException ex)
        {
            logger.LogWarning(ex, "Could not convert Drive file {DriveFileId} to JPEG", driveFileId);
            await Results.Problem(statusCode: 422, title: "This photo couldn't be converted. Download the original instead.").ExecuteAsync(http);
            return;
        }
        catch (DriveApiException ex) when (ex.StatusCode == 404)
        {
            await Results.Problem(statusCode: 404, title: "This file is no longer available.").ExecuteAsync(http);
            return;
        }

        http.Response.Headers.CacheControl = "private, max-age=0, must-revalidate";
        await Results.File(path, "image/jpeg", JpegName(name), lastModified: modified, enableRangeProcessing: true).ExecuteAsync(http);
    }

    private async Task<string> GetOrCreateAsync(string driveFileId, string cacheId, DateTime? modified, CancellationToken ct)
    {
        // Absolute: a relative path (e.g. "App_Data/…" in development) would be served as a web-root virtual file.
        var dir = Path.GetFullPath(Path.Combine(storage.Value.PrivateCachePath, "jpeg", cacheId[..2]));
        var path = Path.Combine(dir, $"{cacheId}-{modified?.Ticks ?? 0}.jpg");
        if (File.Exists(path)) return path;

        await Gate.WaitAsync(ct);
        try
        {
            if (File.Exists(path)) return path; // converted by a parallel request meanwhile

            using var resp = await drive.OpenMediaAsync(driveFileId, null, ct);
            if (resp.StatusCode != HttpStatusCode.OK) throw await DriveClient.ToExceptionAsync(resp, ct);
            await using var original = new MemoryStream();
            await (await resp.Content.ReadAsStreamAsync(ct)).CopyToAsync(original, ct);
            original.Position = 0;

            using var image = new MagickImage(original);
            image.AutoOrient();          // apply the camera's rotation, then drop the tag
            image.Format = MagickFormat.Jpeg;
            image.Quality = 92;
            image.ColorSpace = ColorSpace.sRGB;

            Directory.CreateDirectory(dir);
            var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            // Write through a stream: given a file name, Magick picks the format from its extension (".tmp").
            await using (var output = File.Create(tmp)) await image.WriteAsync(output, MagickFormat.Jpeg, ct);
            File.Move(tmp, path, overwrite: true);
            return path;
        }
        finally { Gate.Release(); }
    }
}
