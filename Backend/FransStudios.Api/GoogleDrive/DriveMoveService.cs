using System.Collections.Concurrent;
using System.Threading.Channels;
using FransStudios.Api.Data;
using FransStudios.Api.Entities;
using FransStudios.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace FransStudios.Api.GoogleDrive;

/// <summary>
/// Moves an album from a previously connected Google account's Drive to the connected one:
/// <list type="number">
/// <item>the old account shares the album folder with the connected account (no email is sent);</item>
/// <item>a new album folder is made in the connected Drive, and Google copies each file into it, keeping the
/// sub-folders (Photos, Videos, …). The bytes never pass through this server;</item>
/// <item>the album switches to the new folder in one step, keeping each file's identity (cover, links, order);</item>
/// <item>the share is removed again.</item>
/// </list>
/// Clients keep seeing the album, from the old Drive, until the switch. The originals stay in the old Drive.
/// A move that stops half-way (error, restart) continues where it left off: copied files are remembered.
/// </summary>
public sealed class DriveMoveService(
    AppDbContext db,
    DriveClient drive,
    DriveTokenService tokens,
    DriveUploadService uploads,
    AlbumSyncService sync,
    AlbumMoveQueue queue,
    AuditService audit,
    ILogger<DriveMoveService> logger)
{
    /// <summary>Queues the album's move (or a retry). Throws <see cref="DriveUploadException"/> when it can't be moved.</summary>
    public async Task RequestAsync(Album album, CancellationToken ct)
    {
        var move = await db.AlbumMoves.SingleOrDefaultAsync(m => m.AlbumId == album.Id, ct);
        if (move is { State: AlbumMoveState.Queued or AlbumMoveState.Running }) return;
        if (album.DriveFolderId is null) throw new DriveUploadException("This album has no Google Drive folder.", 409);
        var connection = await uploads.EnsureCanWriteAsync(ct);
        if (album.DriveAccountEmail is null || DriveTokenService.SameAccount(album.DriveAccountEmail, connection.AccountEmail))
            throw new DriveUploadException("This album is already in the connected Google Drive.", 409);
        if (!await tokens.CanReachAsync(album.DriveAccountEmail, ct))
            throw new DriveUploadException($"Access to {album.DriveAccountEmail}'s Drive is gone, so its files can't be copied. Connect {album.DriveAccountEmail} again, then connect the current account again and move the album.", 409);

        if (move is null) db.AlbumMoves.Add(move = new AlbumMove { AlbumId = album.Id });
        move.State = AlbumMoveState.Queued;
        move.Error = null;
        audit.Add(AuditActions.AlbumMoveRequested, "Album", album.Id, new { from = album.DriveAccountEmail, to = connection.AccountEmail });
        await db.SaveChangesAsync(ct);
        queue.Enqueue(album.Id);
    }

    /// <summary>Runs (or continues) a queued move. Called by <see cref="AlbumMoveWorker"/>.</summary>
    public async Task RunAsync(int albumId, CancellationToken ct)
    {
        var album = await db.Albums.Include(a => a.Move).SingleOrDefaultAsync(a => a.Id == albumId, ct);
        if (album?.Move is not { State: AlbumMoveState.Queued or AlbumMoveState.Running } move) return;

        move.State = AlbumMoveState.Running;
        await db.SaveChangesAsync(ct);
        try
        {
            await MoveAsync(album, move, ct);
        }
        catch (Exception ex) when (ex is DriveApiException or DriveNotConnectedException or DriveUploadException or HttpRequestException
                                       || ex is OperationCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Moving album {AlbumId} to the connected Drive failed", albumId);
            move.State = AlbumMoveState.Failed;
            move.Error = Truncate(Describe(ex), 1000);
            audit.Add(AuditActions.AlbumMoveFailed, "Album", album.Id, new { move.Error });
            await db.SaveChangesAsync(CancellationToken.None);
        }
    }

    private async Task MoveAsync(Album album, AlbumMove move, CancellationToken ct)
    {
        var target = await uploads.EnsureCanWriteAsync(ct);
        if (DriveTokenService.SameAccount(album.DriveAccountEmail, target.AccountEmail))
        {
            // The studio switched back to the account the album is in: nothing to move any more.
            await ForgetCopiesAsync(album, move, ct);
            db.AlbumMoves.Remove(move);
            await db.SaveChangesAsync(ct);
            return;
        }
        // An earlier attempt was copying into another account (the studio connected a different one since):
        // start again, into this one.
        if (move.TargetAccountEmail is not null && !DriveTokenService.SameAccount(move.TargetAccountEmail, target.AccountEmail))
            await ForgetCopiesAsync(album, move, ct);
        move.TargetAccountEmail = target.AccountEmail;
        await db.SaveChangesAsync(ct);

        // The two Drives. The receiving one is fixed to the account connected now: if the studio connects another
        // one while the move runs, it still finishes into this account, and the album then shows as being in that
        // (by then previous) account's Drive, ready to be moved again.
        var oldDrive = drive.ForAccount(await tokens.AccountIdForAsync(album.DriveAccountEmail, ct));
        var newDrive = drive.ForAccount(target.Id);
        var oldFolderId = album.DriveFolderId!;
        var from = album.DriveAccountEmail;

        // 1. Let the receiving account read the old folder (and so every file in it).
        var permissionId = await oldDrive.ShareWithAsync(oldFolderId, target.AccountEmail, ct);

        // 2. The album's new folder (made once; remembered if the move is interrupted, unless it was deleted since).
        if (move.TargetFolderId is not null && !await IsUsableFolderAsync(newDrive, move.TargetFolderId, ct))
            await ForgetCopiesAsync(album, move, ct);
        if (move.TargetFolderId is null)
        {
            move.TargetFolderId = (await uploads.CreateNewAlbumFolderAsync(newDrive, album.Title, ct)).Id;
            await db.SaveChangesAsync(ct);
        }
        var folders = new Dictionary<string, string> { [""] = move.TargetFolderId };

        // 3. Copy the files that aren't copied yet.
        var files = await db.AlbumFiles.Where(f => f.AlbumId == album.Id && !f.IsRemoved).OrderBy(f => f.SortOrder).ToListAsync(ct);
        move.Total = files.Count;
        move.Done = files.Count(f => f.MovedDriveFileId is not null);
        await db.SaveChangesAsync(ct);
        await CopyAsync(newDrive, move, files.Where(f => f.MovedDriveFileId is null), folders, ct);

        // 4. Switch over, while no sync can run: copy anything the sync added meanwhile, then swap the ids.
        await AlbumSyncService.RunExclusiveAsync(album.Id, async () =>
        {
            var late = await db.AlbumFiles.Where(f => f.AlbumId == album.Id && !f.IsRemoved && f.MovedDriveFileId == null).ToListAsync(ct);
            move.Total += late.Count;
            await CopyAsync(newDrive, move, late, folders, ct);

            foreach (var file in await db.AlbumFiles.Where(f => f.AlbumId == album.Id && f.MovedDriveFileId != null).ToListAsync(ct))
            {
                file.DriveFileId = file.MovedDriveFileId!;
                file.MovedDriveFileId = null;
            }
            album.DriveFolderId = move.TargetFolderId;
            album.DriveFolderName = Truncate(await newDrive.GetFolderPathAsync(move.TargetFolderId, ct), 400);
            album.DriveAccountEmail = target.AccountEmail;
            album.LastSyncError = null;
            db.AlbumMoves.Remove(move);
            audit.Add(AuditActions.AlbumMoved, "Album", album.Id, new { from, to = target.AccountEmail, files = move.Done });
            await db.SaveChangesAsync(ct);
        }, ct);
        logger.LogInformation("Moved album {AlbumId} ({Count} files) from {From} to {To}", album.Id, move.Done, from, target.AccountEmail);

        // 5. The old Drive doesn't need to be shared any more. Best effort: the move itself is done.
        try
        {
            await oldDrive.RemovePermissionAsync(oldFolderId, permissionId, ct);
        }
        catch (Exception ex) when (ex is DriveApiException or DriveNotConnectedException or HttpRequestException)
        {
            logger.LogWarning(ex, "Could not remove the temporary share on album {AlbumId}'s old folder", album.Id);
        }

        // Refresh sizes, dates and thumbnails from the new copies.
        await sync.SyncAsync(album.Id, ct);
    }

    /// <summary>Drops what an earlier attempt copied (its folder and copies stay in that Drive, unused).</summary>
    private async Task ForgetCopiesAsync(Album album, AlbumMove move, CancellationToken ct)
    {
        await db.AlbumFiles.Where(f => f.AlbumId == album.Id && f.MovedDriveFileId != null)
            .ExecuteUpdateAsync(u => u.SetProperty(f => f.MovedDriveFileId, (string?)null), ct);
        move.TargetFolderId = null;
        move.TargetAccountEmail = null;
        move.Done = 0;
    }

    private static async Task<bool> IsUsableFolderAsync(DriveClient target, string id, CancellationToken ct)
    {
        try
        {
            var (item, trashed, _) = await target.GetItemAsync(id, ct);
            return item.MimeType == DriveHttp.FolderMime && !trashed;
        }
        catch (DriveApiException ex) when (ex.StatusCode == 404) { return false; }
    }

    private async Task CopyAsync(DriveClient target, AlbumMove move, IEnumerable<AlbumFile> files, Dictionary<string, string> folders,
        CancellationToken ct)
    {
        foreach (var file in files)
        {
            var parent = await FolderForAsync(target, folders, file.Section, ct);
            // A copy may already exist if the previous attempt stopped right after Google made it.
            file.MovedDriveFileId = await target.FindCopyAsync(file.DriveFileId, parent, ct)
                                    ?? await target.CopyFileAsync(file.DriveFileId, parent, file.Name, ct);
            move.Done++;
            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>The new album folder's sub-folder for a section path like "Photos/Ceremony", created as needed.</summary>
    private static async Task<string> FolderForAsync(DriveClient target, Dictionary<string, string> folders, string section, CancellationToken ct)
    {
        if (folders.TryGetValue(section, out var known)) return known;
        var slash = section.LastIndexOf('/');
        var parent = await FolderForAsync(target, folders, slash < 0 ? "" : section[..slash], ct);
        var name = slash < 0 ? section : section[(slash + 1)..];
        var id = await target.FindChildFolderAsync(parent, name, ct) ?? (await target.CreateFolderAsync(parent, name, ct)).Id;
        return folders[section] = id;
    }

    private static string Describe(Exception ex) => ex switch
    {
        DriveApiException { Reason: "storageQuotaExceeded" or "quotaExceeded" } =>
            "The connected Google Drive is full. Free up space (or upgrade its storage), then try again.",
        DriveApiException { StatusCode: 404 } =>
            "A file or folder of this album wasn't found in the old Drive (it may have been deleted). Press “Sync now”, then try again.",
        DriveApiException { StatusCode: 403 } d => $"Google Drive refused the move: {d.Message}",
        DriveNotConnectedException or DriveUploadException => ex.Message,
        OperationCanceledException => "Google took too long to copy a file. Try again: files already copied are kept.",
        _ => "Google Drive could not be reached. Try again in a few minutes: files already copied are kept.",
    };

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}

/// <summary>Albums waiting to be moved, processed one at a time.</summary>
public sealed class AlbumMoveQueue
{
    private readonly Channel<int> _channel = Channel.CreateUnbounded<int>();
    private readonly ConcurrentDictionary<int, byte> _pending = new();

    public void Enqueue(int albumId)
    {
        if (_pending.TryAdd(albumId, 0)) _channel.Writer.TryWrite(albumId);
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

public sealed class AlbumMoveWorker(AlbumMoveQueue queue, IServiceScopeFactory scopes, ILogger<AlbumMoveWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Moves that were queued or running when the server stopped carry on.
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            foreach (var id in await db.AlbumMoves.AsNoTracking()
                         .Where(m => m.State == AlbumMoveState.Queued || m.State == AlbumMoveState.Running)
                         .OrderBy(m => m.UpdatedAt).Select(m => m.AlbumId).ToListAsync(stoppingToken))
                queue.Enqueue(id);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogError(ex, "Could not resume album moves"); }

        await foreach (var albumId in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<DriveMoveService>().RunAsync(albumId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception ex) { logger.LogError(ex, "Moving album {AlbumId} failed", albumId); }
        }
    }
}
