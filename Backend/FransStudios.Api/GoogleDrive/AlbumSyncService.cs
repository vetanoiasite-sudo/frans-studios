using System.Collections.Concurrent;
using System.Threading.Channels;
using FransStudios.Api.Configuration;
using FransStudios.Api.Data;
using FransStudios.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FransStudios.Api.GoogleDrive;

public sealed record SyncResult(bool Success, int FileCount, long TotalBytes, string? Error);

/// <summary>
/// Mirrors an album's Drive folder (recursively) into the AlbumFiles index. Only regular files that
/// physically live under the album folder are indexed: Google Docs and shortcuts are skipped, so a
/// shortcut can never expose a file from elsewhere in the studio's Drive.
/// </summary>
public sealed class AlbumSyncService(
    AppDbContext db,
    DriveClient drive,
    DriveTokenService tokens,
    IOptions<GoogleDriveOptions> options,
    TimeProvider clock,
    ILogger<AlbumSyncService> logger)
{
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> Locks = new();

    public async Task<SyncResult> SyncAsync(int albumId, CancellationToken ct)
    {
        var gate = Locks.GetOrAdd(albumId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try { return await SyncLockedAsync(albumId, ct); }
        finally { gate.Release(); }
    }

    /// <summary>Runs <paramref name="action"/> while no sync of the album is running (and none can start).</summary>
    public static async Task RunExclusiveAsync(int albumId, Func<Task> action, CancellationToken ct)
    {
        var gate = Locks.GetOrAdd(albumId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try { await action(); }
        finally { gate.Release(); }
    }

    private async Task<SyncResult> SyncLockedAsync(int albumId, CancellationToken ct)
    {
        var album = await db.Albums.SingleOrDefaultAsync(a => a.Id == albumId, ct);
        if (album is null) return new SyncResult(false, 0, 0, "Album not found.");
        if (string.IsNullOrEmpty(album.DriveFolderId))
            return await FailAsync(album, "No Google Drive folder is assigned to this album.", ct);

        List<(DriveItem Item, string Section)> found;
        try
        {
            // Read through the account whose Drive holds the folder (a previously connected one, until the album is moved).
            var albumDrive = drive.ForAccount(await tokens.AccountIdForAsync(album.DriveAccountEmail, ct));
            found = await WalkAsync(albumDrive, album.DriveFolderId, ct);
        }
        catch (Exception ex) when (ex is DriveApiException or DriveNotConnectedException or HttpRequestException)
        {
            logger.LogWarning(ex, "Sync failed for album {AlbumId}", albumId);
            var message = ex switch
            {
                DriveApiException { StatusCode: 404 } => "The Google Drive folder was not found. It may have been deleted or moved out of the connected account.",
                DriveNotConnectedException d => d.Message,
                _ => "Google Drive could not be reached. Please try again in a few minutes.",
            };
            return await FailAsync(album, message, ct);
        }

        var existing = await db.AlbumFiles.Where(f => f.AlbumId == albumId).ToDictionaryAsync(f => f.DriveFileId, ct);
        var seen = new HashSet<string>();
        var order = 0;

        foreach (var (item, section) in found
                     .OrderBy(x => SectionRank(x.Section))
                     .ThenBy(x => x.Section, NaturalComparer.Instance)
                     .ThenBy(x => x.Item.Name, NaturalComparer.Instance))
        {
            if (!seen.Add(item.Id)) continue; // a file can have several parents
            if (!existing.TryGetValue(item.Id, out var file))
            {
                file = new AlbumFile { AlbumId = albumId, DriveFileId = item.Id };
                db.AlbumFiles.Add(file);
            }
            var rotated = item.Rotation is 1 or 3;
            file.Name = Truncate(item.Name, 400);
            file.Section = Truncate(section, 400);
            file.MimeType = Truncate(item.MimeType, 128);
            file.Kind = KindOf(item.MimeType);
            file.SizeBytes = item.Size ?? 0;
            file.Width = rotated ? item.Height : item.Width;
            file.Height = rotated ? item.Width : item.Height;
            file.DurationMs = item.DurationMs;
            file.DriveModifiedAt = item.ModifiedTime;
            file.HasThumbnail = item.HasThumbnail;
            file.SortOrder = order++;
            file.IsRemoved = false;
        }

        foreach (var gone in existing.Values.Where(f => !seen.Contains(f.DriveFileId))) gone.IsRemoved = true;

        var live = db.AlbumFiles.Local.Where(f => f.AlbumId == albumId && !f.IsRemoved).ToList();
        album.FileCount = live.Count;
        album.TotalBytes = live.Sum(f => f.SizeBytes);
        album.LastSyncedAt = clock.GetUtcNow().UtcDateTime;
        album.LastSyncError = null;

        var cover = album.CoverFileId is { } coverId ? live.FirstOrDefault(f => f.Id == coverId) : null;
        if (cover is null) album.CoverFileId = DefaultCover(live)?.Id;

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Synced album {AlbumId}: {Count} files, {Bytes} bytes", albumId, album.FileCount, album.TotalBytes);
        return new SyncResult(true, album.FileCount, album.TotalBytes, null);
    }

    private async Task<List<(DriveItem, string)>> WalkAsync(DriveClient albumDrive, string rootId, CancellationToken ct)
    {
        var o = options.Value;
        var files = new List<(DriveItem, string)>();
        var queue = new Queue<(string Id, string Section, int Depth)>();
        queue.Enqueue((rootId, "", 0));
        var visited = new HashSet<string>();

        while (queue.TryDequeue(out var folder))
        {
            if (!visited.Add(folder.Id)) continue;
            foreach (var item in await albumDrive.ListChildrenAsync(folder.Id, ct))
            {
                if (item.MimeType == DriveHttp.FolderMime)
                {
                    if (folder.Depth + 1 <= o.MaxFolderDepth)
                        queue.Enqueue((item.Id, folder.Section.Length == 0 ? item.Name : $"{folder.Section}/{item.Name}", folder.Depth + 1));
                    continue;
                }
                // Google-native documents can't be streamed as bytes; shortcuts could point outside the album.
                if (item.MimeType.StartsWith("application/vnd.google-apps.", StringComparison.Ordinal)) continue;

                files.Add((item, folder.Section));
                if (files.Count > o.MaxFilesPerAlbum)
                    throw new DriveApiException(413, "tooManyFiles", $"Album folder has more than {o.MaxFilesPerAlbum} files.");
            }
        }
        return files;
    }

    private async Task<SyncResult> FailAsync(Album album, string error, CancellationToken ct)
    {
        album.LastSyncError = error;
        await db.SaveChangesAsync(ct);
        return new SyncResult(false, album.FileCount, album.TotalBytes, error);
    }

    /// <summary>The album's first photo; for a films-only album, the first video that has a preview frame.</summary>
    public static AlbumFile? DefaultCover(IEnumerable<AlbumFile> live)
    {
        var ordered = live.OrderBy(f => f.SortOrder).ToList();
        return ordered.FirstOrDefault(f => f.Kind == MediaKind.Image)
               ?? ordered.FirstOrDefault(f => f.Kind == MediaKind.Video && f.HasThumbnail);
    }

    public static MediaKind KindOf(string mime) =>
        mime.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ? MediaKind.Image
        : mime.StartsWith("video/", StringComparison.OrdinalIgnoreCase) ? MediaKind.Video
        : MediaKind.Other;

    /// <summary>Root files first, then photo folders, then video folders, then everything else.</summary>
    private static int SectionRank(string section)
    {
        if (section.Length == 0) return 0;
        var top = section.Split('/')[0];
        if (top.Contains("photo", StringComparison.OrdinalIgnoreCase) || top.Contains("image", StringComparison.OrdinalIgnoreCase)) return 1;
        if (top.Contains("video", StringComparison.OrdinalIgnoreCase) || top.Contains("film", StringComparison.OrdinalIgnoreCase)) return 2;
        return 3;
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}

/// <summary>Sorts "IMG_2.jpg" before "IMG_10.jpg".</summary>
public sealed class NaturalComparer : IComparer<string>
{
    public static readonly NaturalComparer Instance = new();

    public int Compare(string? x, string? y)
    {
        if (x is null || y is null) return string.Compare(x, y, StringComparison.Ordinal);
        int i = 0, j = 0;
        while (i < x.Length && j < y.Length)
        {
            if (char.IsDigit(x[i]) && char.IsDigit(y[j]))
            {
                int si = i, sj = j;
                while (i < x.Length && char.IsDigit(x[i])) i++;
                while (j < y.Length && char.IsDigit(y[j])) j++;
                var a = x[si..i].TrimStart('0');
                var b = y[sj..j].TrimStart('0');
                var c = a.Length != b.Length ? a.Length.CompareTo(b.Length) : string.CompareOrdinal(a, b);
                if (c != 0) return c;
            }
            else
            {
                var c = char.ToLowerInvariant(x[i]).CompareTo(char.ToLowerInvariant(y[j]));
                if (c != 0) return c;
                i++; j++;
            }
        }
        return (x.Length - i).CompareTo(y.Length - j);
    }
}

/// <summary>Background queue so stale albums can be refreshed without making the client wait.</summary>
public sealed class AlbumSyncQueue
{
    private readonly Channel<int> _channel = Channel.CreateBounded<int>(new BoundedChannelOptions(500) { FullMode = BoundedChannelFullMode.DropWrite });
    private readonly ConcurrentDictionary<int, byte> _pending = new();

    public void Enqueue(int albumId)
    {
        if (_pending.TryAdd(albumId, 0) && !_channel.Writer.TryWrite(albumId)) _pending.TryRemove(albumId, out _);
    }

    internal async IAsyncEnumerable<int> ReadAllAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var id in _channel.Reader.ReadAllAsync(ct))
        {
            _pending.TryRemove(id, out _);
            yield return id;
        }
    }
}

public sealed class AlbumSyncWorker(AlbumSyncQueue queue, IServiceScopeFactory scopes, ILogger<AlbumSyncWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var albumId in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<AlbumSyncService>().SyncAsync(albumId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception ex) { logger.LogError(ex, "Background sync failed for album {AlbumId}", albumId); }
        }
    }
}
