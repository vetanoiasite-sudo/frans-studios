using FransStudios.Api.Configuration;
using FransStudios.Api.Entities;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace FransStudios.Api.Storage;

/// <summary>
/// PUBLIC website media only (portfolio images, film posters, web-encoded films). Stored on the
/// server's disk under Storage:PublicMediaPath and served at /media with long-lived caching.
/// Private client deliveries never touch this store — they live in Google Drive.
/// </summary>
public sealed class PublicMediaStore(IOptions<StorageOptions> options, ILogger<PublicMediaStore> logger)
{
    public const string RequestPath = "/media";
    private static readonly int[] TargetWidths = [480, 960, 1600, 2400];
    /// <summary>Full-width page covers also get a 3200 px variant for large high-density screens.</summary>
    public static readonly int[] CoverWidths = [480, 960, 1600, 2400, 3200];
    private static readonly HashSet<string> VideoTypes = new(StringComparer.OrdinalIgnoreCase) { "video/mp4", "video/webm" };

    private string Root => options.Value.PublicMediaPath;

    /// <summary>
    /// Decodes an uploaded image (format detected from content, not file name), auto-orients it,
    /// strips all metadata (EXIF/GPS), and writes WebP + JPEG variants at several widths.
    /// ImageSharp has no AVIF encoder; WebP covers all current browsers with JPEG as fallback.
    /// </summary>
    public Task<ImageAsset> SaveImageAsync(Stream upload, CancellationToken ct) => SaveImageAsync(upload, TargetWidths, ct);

    /// <summary>As above, with explicit target widths (ascending); the image is never upscaled.</summary>
    public async Task<ImageAsset> SaveImageAsync(Stream upload, int[] targetWidths, CancellationToken ct)
    {
        Image image;
        try { image = await Image.LoadAsync(upload, ct); }
        catch (Exception ex) when (ex is UnknownImageFormatException or InvalidImageContentException or NotSupportedException)
        {
            throw new InvalidUploadException("The file is not a supported image (JPEG, PNG, WebP, TIFF, BMP or GIF).");
        }

        using (image)
        {
            image.Mutate(x => x.AutoOrient());
            image.Metadata.ExifProfile = null;
            image.Metadata.IptcProfile = null;
            image.Metadata.XmpProfile = null;

            var key = $"img/{Guid.NewGuid():N}";
            var dir = FullPath(key);
            Directory.CreateDirectory(dir);

            var widths = targetWidths.Where(w => w < image.Width).ToList();
            widths.Add(Math.Min(image.Width, targetWidths[^1]));
            widths = widths.Distinct().Order().ToList();

            try
            {
                foreach (var w in widths)
                {
                    using var resized = w == image.Width ? image.Clone(_ => { }) : image.Clone(x => x.Resize(w, 0, KnownResamplers.Lanczos3));
                    await resized.SaveAsWebpAsync(Path.Combine(dir, $"{w}.webp"), new WebpEncoder { FileFormat = WebpFileFormatType.Lossy, Quality = 80, Method = WebpEncodingMethod.Level5 }, ct);
                    await resized.SaveAsJpegAsync(Path.Combine(dir, $"{w}.jpg"), new JpegEncoder { Quality = 82 }, ct);
                }
            }
            catch
            {
                TryDelete(key);
                throw;
            }

            var largest = widths[^1];
            return new ImageAsset
            {
                Key = key,
                Width = largest,
                Height = (int)Math.Round(image.Height * (largest / (double)image.Width)),
                Widths = string.Join(',', widths),
            };
        }
    }

    /// <summary>Stores a web-encoded film (MP4/WebM). Re-encoding is the editor's job (1080p H.264, fast start).</summary>
    public async Task<(string Key, long Bytes)> SaveVideoAsync(Stream upload, string contentType, CancellationToken ct)
    {
        if (!VideoTypes.Contains(contentType))
            throw new InvalidUploadException("Films must be MP4 (H.264) or WebM.");

        var ext = contentType.EndsWith("webm", StringComparison.OrdinalIgnoreCase) ? ".webm" : ".mp4";
        var key = $"video/{Guid.NewGuid():N}{ext}";
        var path = FullPath(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using (var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            await upload.CopyToAsync(fs, 81920, ct);

        // Cheap sanity check: MP4 has "ftyp" at offset 4, WebM starts with the EBML magic.
        var header = new byte[12];
        await using (var check = File.OpenRead(path)) _ = await check.ReadAsync(header, ct);
        var ok = ext == ".mp4"
            ? header[4] == 'f' && header[5] == 't' && header[6] == 'y' && header[7] == 'p'
            : header[0] == 0x1A && header[1] == 0x45 && header[2] == 0xDF && header[3] == 0xA3;
        if (!ok)
        {
            TryDelete(key);
            throw new InvalidUploadException("The file does not look like a valid video.");
        }
        return (key, new FileInfo(path).Length);
    }

    public void TryDelete(string? key)
    {
        if (string.IsNullOrEmpty(key)) return;
        try
        {
            var path = FullPath(key);
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
            else if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException ex) { logger.LogWarning(ex, "Could not delete public media {Key}", key); }
    }

    /// <summary>Resolves a key to a path, refusing anything that would escape the media root.</summary>
    private string FullPath(string key)
    {
        var root = Path.GetFullPath(Root);
        var full = Path.GetFullPath(Path.Combine(root, key));
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Invalid media key.");
        return full;
    }
}

public sealed class InvalidUploadException(string message) : Exception(message);
