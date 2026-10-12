namespace FransStudios.Api.Entities;

// ─────────────────────────────────────────────────────────────────────────────
// Identity & private delivery
// ─────────────────────────────────────────────────────────────────────────────

public enum UserRole : byte { Admin = 1, Client = 2 }

public interface ISoftDeletable { DateTime? DeletedAt { get; set; } }

public interface ITimestamped
{
    DateTime CreatedAt { get; set; }
    DateTime UpdatedAt { get; set; }
}

/// <summary>
/// A login. Admins have no ClientId; client logins point at a Client.
/// Several Users may share one Client (future "family member" logins) — V1 creates exactly one.
/// </summary>
public class User : ISoftDeletable, ITimestamped
{
    public int Id { get; set; }
    public string Username { get; set; } = "";          // normalised: lower-case, trimmed
    public string PasswordHash { get; set; } = "";
    public UserRole Role { get; set; }
    public int? ClientId { get; set; }
    public Client? Client { get; set; }
    public bool IsActive { get; set; } = true;
    public int FailedLoginCount { get; set; }
    public DateTime? LockoutEndUtc { get; set; }
    /// <summary>Rotated whenever credentials or access change; invalidates existing sessions.</summary>
    public Guid SecurityStamp { get; set; } = Guid.NewGuid();
    public DateTime? LastLoginAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }

    public List<Session> Sessions { get; set; } = [];
}

public class Client : ISoftDeletable, ITimestamped
{
    public int Id { get; set; }
    public string FullName { get; set; } = "";
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }

    public List<User> Users { get; set; } = [];
    public List<Album> Albums { get; set; } = [];
}

public enum AlbumStatus : byte { Draft = 0, Published = 1, Disabled = 2 }

/// <summary>A private client delivery album, backed by one Google Drive folder.</summary>
public class Album : ISoftDeletable, ITimestamped
{
    public int Id { get; set; }
    /// <summary>Non-sequential identifier used in client-facing URLs.</summary>
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public int ClientId { get; set; }
    public Client Client { get; set; } = null!;
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public DateOnly? EventDate { get; set; }
    public AlbumStatus Status { get; set; } = AlbumStatus.Draft;
    public bool DownloadsEnabled { get; set; } = true;

    public string? DriveFolderId { get; set; }
    public string? DriveFolderName { get; set; }
    /// <summary>
    /// The Google account whose Drive holds <see cref="DriveFolderId"/>. After the studio connects another account,
    /// albums still on the previous one keep working through that account's stored access until they are moved.
    /// </summary>
    public string? DriveAccountEmail { get; set; }
    /// <summary>Set while the album is being moved to the connected account's Drive (or the move failed).</summary>
    public AlbumMove? Move { get; set; }

    public Guid? CoverFileId { get; set; }
    public AlbumFile? CoverFile { get; set; }

    // Denormalised by the sync so listings don't need to aggregate.
    public int FileCount { get; set; }
    public long TotalBytes { get; set; }
    public DateTime? LastSyncedAt { get; set; }
    public string? LastSyncError { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }

    public List<AlbumFile> Files { get; set; } = [];
}

public enum AlbumMoveState : byte { Queued = 1, Running = 2, Failed = 3 }

public enum MediaKind : byte { Image = 1, Video = 2, Other = 3 }

/// <summary>
/// Index of a file inside an album's Drive folder. Clients only ever see <see cref="Id"/>;
/// <see cref="DriveFileId"/> never leaves the server.
/// </summary>
public class AlbumFile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int AlbumId { get; set; }
    public Album Album { get; set; } = null!;
    public string DriveFileId { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>Path of the sub-folder relative to the album folder, e.g. "Photos/Ceremony". Empty for root.</summary>
    public string Section { get; set; } = "";
    public string MimeType { get; set; } = "";
    public MediaKind Kind { get; set; }
    public long SizeBytes { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public long? DurationMs { get; set; }
    public int SortOrder { get; set; }
    public DateTime? DriveModifiedAt { get; set; }
    public bool HasThumbnail { get; set; }
    public bool IsRemoved { get; set; }
    /// <summary>Id of this file's copy in the new Drive while the album is being moved; swapped in when the move finishes.</summary>
    public string? MovedDriveFileId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// An album being moved from a previously connected Google account's Drive into the connected one (see
/// DriveMoveService). Exists while the move is queued, running or failed; removed once the album has switched over.
/// The copies made so far are on the files (<see cref="AlbumFile.MovedDriveFileId"/>).
/// </summary>
public class AlbumMove : ITimestamped
{
    public int AlbumId { get; set; }
    public Album Album { get; set; } = null!;
    public AlbumMoveState State { get; set; } = AlbumMoveState.Queued;
    /// <summary>The account the files are copied into (the one connected when the move started).</summary>
    public string? TargetAccountEmail { get; set; }
    /// <summary>The album's new folder, in <see cref="TargetAccountEmail"/>'s Drive.</summary>
    public string? TargetFolderId { get; set; }
    public int Done { get; set; }
    public int Total { get; set; }
    public string? Error { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class Session
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public DateTime LastSeenAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
}

/// <summary>The studio's Google account link. At most one row has RevokedAt == null.</summary>
public class GoogleDriveConnection
{
    public int Id { get; set; }
    public string AccountEmail { get; set; } = "";
    /// <summary>Refresh token encrypted with ASP.NET Core Data Protection.</summary>
    public string EncryptedRefreshToken { get; set; } = "";
    public string Scopes { get; set; } = "";
    public int ConnectedByUserId { get; set; }
    public User ConnectedByUser { get; set; } = null!;
    public DateTime ConnectedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
}

public class AuditLog
{
    public long Id { get; set; }
    public int? UserId { get; set; }
    public User? User { get; set; }
    public string Action { get; set; } = "";
    public string? EntityType { get; set; }
    public string? EntityId { get; set; }
    public string? IpAddress { get; set; }
    public string? Details { get; set; }
    public DateTime CreatedAt { get; set; }
}

// ─────────────────────────────────────────────────────────────────────────────
// Public website content (stored on the server's public media store — never Drive)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>A processed public image: WebP + JPEG variants at several widths under one storage key.</summary>
public class ImageAsset
{
    /// <summary>Folder key inside the public media store, e.g. "img/3f2a…".</summary>
    public string Key { get; set; } = "";
    public int Width { get; set; }
    public int Height { get; set; }
    /// <summary>Comma-separated list of generated widths, e.g. "480,960,1600".</summary>
    public string Widths { get; set; } = "";
}

public enum GalleryCategory : byte { Wedding = 1, Engagement = 2, Destination = 3, Other = 4 }

public class PublicGallery : ITimestamped
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Slug { get; set; } = "";
    public GalleryCategory Category { get; set; } = GalleryCategory.Wedding;
    public string? Location { get; set; }
    public DateOnly? EventDate { get; set; }
    public string? Intro { get; set; }
    public string? SeoDescription { get; set; }
    public int? CoverImageId { get; set; }
    public PublicImage? CoverImage { get; set; }
    public bool IsPublished { get; set; }
    public bool IsFeatured { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public List<PublicImage> Images { get; set; } = [];
}

/// <summary>
/// Photos uploaded straight to the Portfolio wall (Admin → Portfolio → Upload photos) live in one built-in,
/// always-published gallery with this slug. It feeds the wall only: it is never listed as a story, has no
/// story page and is left out of the sitemap.
/// </summary>
public static class PortfolioWall
{
    public const string Slug = "portfolio-wall";
    public const string Title = "Portfolio";

    /// <summary>Published galleries that are real stories (excludes the built-in wall gallery).</summary>
    public static IQueryable<PublicGallery> PublishedStories(this IQueryable<PublicGallery> q) =>
        q.Where(g => g.IsPublished && g.Slug != Slug);
}

public class PublicImage
{
    public int Id { get; set; }
    public int GalleryId { get; set; }
    public PublicGallery Gallery { get; set; } = null!;
    public ImageAsset Image { get; set; } = new();
    public string AltText { get; set; } = "";
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class PublicFilm : ITimestamped
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public ImageAsset? Poster { get; set; }
    /// <summary>Key of the web-optimised video file in the public media store.</summary>
    public string? VideoKey { get; set; }
    public string? VideoMimeType { get; set; }
    public long? VideoBytes { get; set; }
    public bool IsPublished { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>Optional package tiers. While none are published the Packages page is an inquiry form only.</summary>
public class Package : ITimestamped
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Summary { get; set; }
    public decimal? Price { get; set; }
    public string? Currency { get; set; }
    /// <summary>Free text shown instead of / next to the price, e.g. "Starting from".</summary>
    public string? PriceNote { get; set; }
    /// <summary>One feature per line.</summary>
    public string? Features { get; set; }
    public bool IsPublished { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public enum InquiryStatus : byte { New = 0, Contacted = 1, Booked = 2, Closed = 3 }

/// <summary>Which website form a request came from: the Packages page or the Contact page.</summary>
public enum InquiryOrigin : byte { Packages = 1, Contact = 2 }

public class Inquiry
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Mobile { get; set; } = "";
    public string? Email { get; set; }
    public string? EventType { get; set; }
    public DateOnly? EventDate { get; set; }
    public string? Venue { get; set; }
    public string? Coverage { get; set; }
    public string? Source { get; set; }
    public string? Message { get; set; }
    public InquiryStatus Status { get; set; } = InquiryStatus.New;
    public InquiryOrigin Origin { get; set; } = InquiryOrigin.Packages;
    public string? IpAddress { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Cover photo of a public page (e.g. Packages, Contact), uploaded in Admin → Page covers.
/// One row per page; a page without a row shows its built-in default.
/// </summary>
public class PageCover : ITimestamped
{
    /// <summary>Page key, one of <see cref="Keys"/>.</summary>
    public string Page { get; set; } = "";
    public ImageAsset Image { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public const string Packages = "packages";
    public const string Contact = "contact";
    public const string About = "about";
    public static readonly string[] Keys = [Packages, Contact, About];
}

/// <summary>Small key/value settings changed from the admin (e.g. the Drive folder new albums are created in).</summary>
public class SiteSetting
{
    public string Key { get; set; } = "";
    public string? Value { get; set; }
    public DateTime UpdatedAt { get; set; }

    public const string DriveUploadRootId = "drive.uploadRoot.id";
    public const string DriveUploadRootName = "drive.uploadRoot.name";
}
