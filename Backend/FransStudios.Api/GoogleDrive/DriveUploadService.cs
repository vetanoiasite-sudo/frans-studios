using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using FransStudios.Api.Configuration;
using FransStudios.Api.Data;
using FransStudios.Api.Entities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FransStudios.Api.GoogleDrive;

/// <summary>A problem the admin can act on (shown as-is), with the HTTP status to answer with.</summary>
public sealed class DriveUploadException(string message, int statusCode = 400) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

/// <summary>
/// What the admin's browser needs to upload one file. <see cref="SessionUrl"/> is Google's resumable session
/// URL (Direct mode) — it authorises uploading this one file and nothing else. In Proxy mode it is null and
/// chunks go through <c>/uploads/chunk</c>. <see cref="UploadId"/> is an encrypted, 7-day ticket the browser
/// sends back for status / chunk / complete calls; the server never trusts a URL from the browser.
/// </summary>
public sealed record UploadSession(string UploadId, string? SessionUrl, int ChunkSize, string Mode);

/// <summary>
/// Uploads into the studio's Google Drive: the folder new albums are created in, one folder per album
/// (with Photos / Videos inside), and resumable upload sessions for each file. Files uploaded here and files
/// the studio puts in Drive by hand end up in the same place and are indexed by the same album sync.
/// </summary>
public sealed class DriveUploadService(
    AppDbContext db,
    DriveClient drive,
    DriveTokenService tokens,
    AlbumSyncQueue syncQueue,
    IDataProtectionProvider dataProtection,
    IOptions<GoogleDriveOptions> options,
    IOptions<SiteOptions> site,
    TimeProvider clock)
{
    public static readonly string[] DefaultSections = ["Photos", "Videos"];
    private const long MaxFileBytes = 5L * 1024 * 1024 * 1024 * 1024; // Google Drive's per-file limit (5 TB)

    private static readonly ConcurrentDictionary<int, SemaphoreSlim> FolderLocks = new();

    /// <summary>Uploads from the admin Drive manager aren't tied to an album: their tickets use this scope.</summary>
    public const int AnyFolderScope = 0;

    /// <summary>AlbumId is the album the upload was started for, or <see cref="AnyFolderScope"/>.</summary>
    private sealed record UploadTicket(int AlbumId, string SessionUrl, string ParentId, string Name, long Size);

    private ITimeLimitedDataProtector Protector =>
        dataProtection.CreateProtector("FransStudios.DriveUpload.Ticket.v1").ToTimeLimitedDataProtector();

    private int ChunkSize => options.Value.UploadChunkMb * 1024 * 1024; // a multiple of 256 KB

    // ── Permission ──────────────────────────────────────────────────────────

    /// <summary>Throws unless Drive is connected with a scope that allows uploading; returns that connection.</summary>
    public async Task<GoogleDriveConnection> EnsureCanWriteAsync(CancellationToken ct)
    {
        var connection = await tokens.GetActiveConnectionAsync(ct)
                         ?? throw new DriveUploadException("Google Drive is not connected. Connect it in Admin → Google Drive.", 409);
        if (!GoogleDriveOptions.CanWrite(connection.Scopes))
            throw new DriveUploadException("Google Drive is connected with read-only access. Reconnect it in Admin → Google Drive to allow uploads.", 409);
        return connection;
    }

    // ── The folder album folders are created in ─────────────────────────────

    /// <summary>Album folders go in this folder in My Drive. The website creates it the first time it's needed.</summary>
    public const string AlbumsRootName = "Frans Studios Albums";

    /// <summary>
    /// The "Frans Studios Albums" folder in <paramref name="target"/>'s My Drive: the remembered one if it's still
    /// there, else an existing folder with that name, else a new one. Looking it up again covers switching Google
    /// accounts (the remembered folder is then in the other account's Drive) and the folder being deleted.
    /// </summary>
    private async Task<(string Id, string Name)> EnsureAlbumsRootAsync(DriveClient target, CancellationToken ct)
    {
        const string path = $"My Drive / {AlbumsRootName}";
        var saved = await db.SiteSettings.AsNoTracking().Where(s => s.Key == SiteSetting.DriveUploadRootId)
            .Select(s => s.Value).SingleOrDefaultAsync(ct);
        if (saved is { Length: > 0 })
        {
            try
            {
                var (item, trashed, _) = await target.GetItemAsync(saved, ct);
                if (!trashed && item.MimeType == DriveHttp.FolderMime && item.Name == AlbumsRootName) return (item.Id, path);
            }
            catch (DriveApiException ex) when (ex.StatusCode == 404) { }
        }

        var id = await target.FindChildFolderAsync("root", AlbumsRootName, ct)
                 ?? (await target.CreateFolderAsync("root", AlbumsRootName, ct)).Id;
        await SaveRootAsync(id, path, ct);
        return (id, path);
    }

    private async Task SaveRootAsync(string id, string name, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        foreach (var (key, value) in new[] { (SiteSetting.DriveUploadRootId, id), (SiteSetting.DriveUploadRootName, Truncate(name, 1000)) })
        {
            var row = await db.SiteSettings.SingleOrDefaultAsync(s => s.Key == key, ct);
            if (row is null) db.SiteSettings.Add(row = new SiteSetting { Key = key });
            row.Value = value;
            row.UpdatedAt = now;
        }
        await db.SaveChangesAsync(ct);
    }

    // ── Album folders ───────────────────────────────────────────────────────

    /// <summary>
    /// Creates the album's folder inside "Frans Studios Albums" (named after the album, made unique) with
    /// Photos and Videos inside, and assigns it to the album. The caller saves the album.
    /// </summary>
    public async Task CreateAlbumFolderAsync(Album album, CancellationToken ct)
    {
        var connection = await EnsureCanWriteAsync(ct);
        var (id, path) = await CreateNewAlbumFolderAsync(drive, album.Title, ct);
        album.DriveFolderId = id;
        album.DriveAccountEmail = connection.AccountEmail;
        album.DriveFolderName = Truncate(path, 400);
    }

    /// <summary>
    /// A new folder for an album in <paramref name="target"/>'s Drive: in "Frans Studios Albums", named after the
    /// album (made unique), with Photos and Videos inside. Returns its id and display path.
    /// </summary>
    public async Task<(string Id, string Path)> CreateNewAlbumFolderAsync(DriveClient target, string title, CancellationToken ct)
    {
        var root = await EnsureAlbumsRootAsync(target, ct);
        var baseName = CleanName(title, "Album title");
        var name = baseName;
        for (var n = 2; await target.FindChildFolderAsync(root.Id, name, ct) is not null; n++)
        {
            if (n > 50) throw new DriveUploadException("Too many folders with this album's name already exist in Drive.", 409);
            name = $"{baseName} ({n})";
        }

        var folder = await target.CreateFolderAsync(root.Id, name, ct);
        foreach (var section in DefaultSections) await target.CreateFolderAsync(folder.Id, section, ct);
        return (folder.Id, $"{root.Name} / {folder.Name}");
    }

    /// <summary>Id of the album's sub-folder for <paramref name="section"/>, creating it if needed.</summary>
    private async Task<string> EnsureSectionFolderAsync(Album album, string section, CancellationToken ct)
    {
        var gate = FolderLocks.GetOrAdd(album.Id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            return await drive.FindChildFolderAsync(album.DriveFolderId!, section, ct)
                   ?? (await drive.CreateFolderAsync(album.DriveFolderId!, section, ct)).Id;
        }
        finally { gate.Release(); }
    }

    // ── Resumable uploads ───────────────────────────────────────────────────

    public async Task<UploadSession> StartAsync(Album album, string fileName, long size, string? mimeType, string section, CancellationToken ct)
    {
        var connection = await EnsureCanWriteAsync(ct);
        if (album.DriveFolderId is null) throw new DriveUploadException("Create or choose this album's Google Drive folder first.", 409);
        if (await db.AlbumMoves.AnyAsync(m => m.AlbumId == album.Id && m.State != AlbumMoveState.Failed, ct))
            throw new DriveUploadException("This album is being moved to the current Google Drive. Upload when the move has finished.", 409);
        if (album.DriveAccountEmail is not null && !DriveTokenService.SameAccount(album.DriveAccountEmail, connection.AccountEmail))
            throw new DriveUploadException($"This album is still in {album.DriveAccountEmail}'s Drive. Move it to the current Drive first, then upload.", 409);
        if (size <= 0) throw new DriveUploadException("The file is empty.");
        if (size > MaxFileBytes) throw new DriveUploadException("Google Drive files can be at most 5 TB.");

        var sectionName = CleanSection(section);
        var parentId = await EnsureSectionFolderAsync(album, sectionName, ct);
        return await OpenSessionAsync(album.Id, parentId, fileName, size, mimeType, ct);
    }

    /// <summary>Admin Drive manager: starts an upload straight into any folder of the studio's Drive.</summary>
    public async Task<UploadSession> StartInFolderAsync(string parentId, string fileName, long size, string? mimeType, CancellationToken ct)
    {
        await EnsureCanWriteAsync(ct);
        if (!DriveClient.IsValidId(parentId)) throw new DriveUploadException("Invalid folder.");
        if (size <= 0) throw new DriveUploadException("The file is empty.");
        if (size > MaxFileBytes) throw new DriveUploadException("Google Drive files can be at most 5 TB.");
        return await OpenSessionAsync(AnyFolderScope, parentId, fileName, size, mimeType, ct);
    }

    private async Task<UploadSession> OpenSessionAsync(int scope, string parentId, string fileName, long size, string? mimeType, CancellationToken ct)
    {
        var name = CleanName(fileName, "File name");
        var mime = string.IsNullOrWhiteSpace(mimeType) || mimeType.Length > 128 ? "application/octet-stream" : mimeType.Trim();
        var proxy = options.Value.ProxyUploads;
        var sessionUrl = await drive.StartResumableUploadAsync(parentId, name, mime, size, proxy ? null : SiteOrigin, ct);

        var ticket = Protector.Protect(JsonSerializer.Serialize(new UploadTicket(scope, sessionUrl, parentId, name, size)), TimeSpan.FromDays(7));
        return new UploadSession(ticket, proxy ? null : sessionUrl, ChunkSize, proxy ? "proxy" : "direct");
    }

    /// <summary>How many bytes Google has stored, and the file id once the upload is complete.</summary>
    public async Task<UploadProgress> StatusAsync(int albumId, string uploadId, CancellationToken ct)
    {
        var t = ReadTicket(albumId, uploadId);
        try { return await drive.QueryUploadAsync(t.SessionUrl, t.Size, ct); }
        catch (DriveApiException ex) when (ex.StatusCode is 404 or 410)
        {
            throw new DriveUploadException("This upload expired at Google. Start the file again.", 410);
        }
    }

    /// <summary>Proxy mode: forwards one chunk from the browser to Google.</summary>
    public async Task<UploadProgress> ChunkAsync(int albumId, string uploadId, long offset, long length, Stream body, CancellationToken ct)
    {
        var t = ReadTicket(albumId, uploadId);
        if (offset < 0 || length <= 0 || offset + length > t.Size) throw new DriveUploadException("Chunk is outside the file.");
        if (length != ChunkSize && offset + length != t.Size) throw new DriveUploadException($"Chunks must be {ChunkSize} bytes, except the last one.");
        try { return await drive.PutChunkAsync(t.SessionUrl, body, offset, length, t.Size, ct); }
        catch (DriveApiException ex) when (ex.StatusCode is 404 or 410)
        {
            throw new DriveUploadException("This upload expired at Google. Start the file again.", 410);
        }
    }

    /// <summary>
    /// Called when the browser has sent the last byte. Checks with Google that the file really is complete and
    /// sits in the album's folder, then queues the album sync so it shows up in the album.
    /// </summary>
    public async Task<string> CompleteAsync(Album album, string uploadId, CancellationToken ct)
    {
        var (fileId, _) = await CompleteCoreAsync(album.Id, uploadId, ct);
        syncQueue.Enqueue(album.Id);
        return fileId;
    }

    /// <summary>Admin Drive manager: confirms an upload and refreshes any album the folder belongs to.</summary>
    public async Task<string> CompleteInFolderAsync(string uploadId, CancellationToken ct)
    {
        var (fileId, parentId) = await CompleteCoreAsync(AnyFolderScope, uploadId, ct);
        await EnqueueAlbumsAroundAsync([parentId], ct);
        return fileId;
    }

    private async Task<(string FileId, string ParentId)> CompleteCoreAsync(int scope, string uploadId, CancellationToken ct)
    {
        var t = ReadTicket(scope, uploadId);
        var progress = await StatusAsync(scope, uploadId, ct);
        if (progress.FileId is null)
            throw new DriveUploadException($"Google has {progress.Received} of {t.Size} bytes; the upload isn't finished.", 409);

        var (item, _, parents) = await drive.GetItemAsync(progress.FileId, ct);
        if (!parents.Contains(t.ParentId)) throw new DriveUploadException("The uploaded file is not in the folder it was sent to.", 409);
        if (item.Size is { } stored && stored != t.Size) throw new DriveUploadException("The uploaded file's size doesn't match.", 409);
        return (item.Id, t.ParentId);
    }

    /// <summary>
    /// After a change made from the Drive manager in these folders, re-syncs albums whose folder is one of them
    /// or their parent (an album's files usually sit one level down, in Photos / Videos).
    /// </summary>
    public async Task EnqueueAlbumsAroundAsync(IEnumerable<string> folderIds, CancellationToken ct)
    {
        var ids = new HashSet<string>(folderIds.Where(DriveClient.IsValidId));
        foreach (var id in ids.ToList())
        {
            try { foreach (var parent in (await drive.GetItemAsync(id, ct)).Parents) ids.Add(parent); }
            catch (DriveApiException) { /* folder gone or not readable: nothing to add */ }
        }
        var albumIds = await db.Albums.AsNoTracking().Where(a => a.DriveFolderId != null && ids.Contains(a.DriveFolderId))
            .Select(a => a.Id).ToListAsync(ct);
        foreach (var albumId in albumIds) syncQueue.Enqueue(albumId);
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private UploadTicket ReadTicket(int albumId, string uploadId)
    {
        UploadTicket? t;
        try { t = JsonSerializer.Deserialize<UploadTicket>(Protector.Unprotect(uploadId)); }
        catch (Exception ex) when (ex is CryptographicException or JsonException or FormatException)
        {
            throw new DriveUploadException("This upload is no longer valid. Start the file again.", 410);
        }
        if (t is null || t.AlbumId != albumId) throw new DriveUploadException("This upload belongs somewhere else.", 404);
        return t;
    }

    private string SiteOrigin => new Uri(site.Value.PublicBaseUrl).GetLeftPart(UriPartial.Authority);

    private static string CleanName(string? value, string what)
    {
        var name = (value ?? "").Trim();
        if (name.Length == 0) throw new DriveUploadException($"{what} is empty.");
        if (name.Length > 255) throw new DriveUploadException($"{what} is longer than 255 characters.");
        if (name.Any(char.IsControl)) throw new DriveUploadException($"{what} contains invalid characters.");
        return name;
    }

    private static string CleanSection(string? value)
    {
        var name = CleanName(value, "Section");
        if (name.Length > 100 || name.Contains('/') || name.Contains('\\') || name is "." or "..")
            throw new DriveUploadException("Section names can't contain slashes and must be under 100 characters.");
        return name;
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
