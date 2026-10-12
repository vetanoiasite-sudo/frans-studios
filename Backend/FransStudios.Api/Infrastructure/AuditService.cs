using System.Text.Json;
using FransStudios.Api.Authentication;
using FransStudios.Api.Data;
using FransStudios.Api.Entities;

namespace FransStudios.Api.Infrastructure;

public sealed class AuditService(AppDbContext db, CurrentUser currentUser, IHttpContextAccessor http)
{
    /// <summary>Adds an audit row to the current unit of work (saved with the caller's SaveChanges).</summary>
    public void Add(string action, string? entityType = null, object? entityId = null, object? details = null, int? userId = null)
    {
        db.AuditLogs.Add(new AuditLog
        {
            UserId = userId ?? currentUser.UserIdOrNull,
            Action = action,
            EntityType = entityType,
            EntityId = entityId?.ToString(),
            IpAddress = http.HttpContext?.Connection.RemoteIpAddress?.ToString(),
            Details = details is null ? null : Truncate(JsonSerializer.Serialize(details), 4000),
        });
    }

    /// <summary>Adds and saves immediately.</summary>
    public async Task WriteAsync(string action, string? entityType = null, object? entityId = null, object? details = null,
        int? userId = null, CancellationToken ct = default)
    {
        Add(action, entityType, entityId, details, userId);
        await db.SaveChangesAsync(ct);
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}

public static class AuditActions
{
    public const string LoginSuccess = "login.success";
    public const string LoginFailed = "login.failed";
    public const string LoginLockedOut = "login.locked_out";
    public const string Logout = "logout";
    public const string PasswordChanged = "password.changed";
    public const string PasswordReset = "password.reset";
    public const string ClientCreated = "client.created";
    public const string ClientUpdated = "client.updated";
    public const string ClientDisabled = "client.disabled";
    public const string ClientEnabled = "client.enabled";
    public const string ClientDeleted = "client.deleted";
    public const string AlbumCreated = "album.created";
    public const string AlbumUpdated = "album.updated";
    public const string AlbumFolderAssigned = "album.folder_assigned";
    public const string AlbumMoveRequested = "album.move_requested";
    public const string AlbumMoved = "album.moved";
    public const string AlbumMoveFailed = "album.move_failed";
    public const string AlbumDeleted = "album.deleted";
    public const string AlbumSynced = "album.synced";
    public const string DriveConnected = "drive.connected";
    public const string DriveDisconnected = "drive.disconnected";
    public const string FileDownloaded = "file.downloaded";
    public const string SectionDownloaded = "section.downloaded";
    public const string AlbumFolderCreated = "album.folder_created";
    public const string FileUploaded = "file.uploaded";
    public const string DriveFolderCreated = "drive.folder_created";
    public const string DriveItemRenamed = "drive.item_renamed";
    public const string DriveItemsMoved = "drive.items_moved";
    public const string DriveItemsTrashed = "drive.items_trashed";
}
