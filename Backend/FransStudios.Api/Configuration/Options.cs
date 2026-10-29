using System.ComponentModel.DataAnnotations;

namespace FransStudios.Api.Configuration;

// Strongly-typed settings. Non-secret defaults live in appsettings.json; secrets come from
// environment variables (e.g. GoogleDrive__ClientSecret) or `dotnet user-secrets` in development.

public sealed class SiteOptions
{
    public const string Section = "Site";
    /// <summary>Canonical public origin, e.g. https://fransstudios.com (no trailing slash).</summary>
    [Required, Url] public string PublicBaseUrl { get; set; } = "";
    public string SiteName { get; set; } = "Vetanoia";
    /// <summary>Only needed if the SPA is ever served from a different origin than the API.</summary>
    public string[] AllowedCorsOrigins { get; set; } = [];
    /// <summary>Old WordPress paths → new paths (301).</summary>
    public Dictionary<string, string> LegacyRedirects { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Absolute or site-relative image used for link previews when a page has no image of its own.</summary>
    public string? DefaultShareImage { get; set; }
    public BusinessInfo Business { get; set; } = new();

    /// <summary>Used for schema.org structured data on the home page.</summary>
    public sealed class BusinessInfo
    {
        public string? Email { get; set; }
        public string? Telephone { get; set; }
        public string? Instagram { get; set; }
        public string? Locality { get; set; }
        public string? CountryCode { get; set; }
    }
}

public sealed class DatabaseOptions
{
    public const string Section = "Database";
    public bool MigrateOnStartup { get; set; }
}

public sealed class AuthOptions
{
    public const string Section = "Auth";
    [Range(1, 365)] public int ClientSessionDays { get; set; } = 30;
    [Range(1, 72)] public int AdminSessionHours { get; set; } = 12;
    [Range(3, 20)] public int MaxFailedLogins { get; set; } = 5;
    [Range(1, 1440)] public int LockoutMinutes { get; set; } = 15;
    [Range(8, 128)] public int MinPasswordLength { get; set; } = 10;
}

public sealed class StorageOptions
{
    public const string Section = "Storage";
    /// <summary>Public portfolio media (optimised images, web videos). Served at /media.</summary>
    [Required] public string PublicMediaPath { get; set; } = "";
    /// <summary>Private thumbnail cache for client Drive files. Never served statically.</summary>
    [Required] public string PrivateCachePath { get; set; } = "";
    /// <summary>ASP.NET Core Data Protection key ring. Losing it invalidates cookies and the Drive token.</summary>
    [Required] public string DataProtectionKeysPath { get; set; } = "";
    [Range(1, 200)] public int MaxImageUploadMb { get; set; } = 60;
    [Range(1, 4000)] public int MaxVideoUploadMb { get; set; } = 2000;
}

public sealed class GoogleDriveOptions
{
    public const string Section = "GoogleDrive";
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    /// <summary>
    /// Full Drive access: the site reads albums the studio fills by hand AND uploads into the studio's
    /// Drive from the admin. Works for personal Gmail and Google Workspace alike. Connections made earlier
    /// with drive.readonly keep working for reading; uploads ask the admin to reconnect.
    /// </summary>
    public string Scopes { get; set; } = "https://www.googleapis.com/auth/drive";
    /// <summary>
    /// "Direct": the admin's browser sends file chunks straight to Google (the server only opens the
    /// session). "Proxy": chunks go through this server — use if Google ever refuses browser uploads.
    /// </summary>
    public string UploadMode { get; set; } = "Direct";
    /// <summary>Chunk size for resumable uploads, in MB (Google needs multiples of 256 KB).</summary>
    [Range(1, 64)] public int UploadChunkMb { get; set; } = 16;
    [Range(1, 1440)] public int SyncStaleMinutes { get; set; } = 15;
    [Range(1, 20)] public int MaxFolderDepth { get; set; } = 6;
    [Range(100, 100_000)] public int MaxFilesPerAlbum { get; set; } = 20_000;
    [Range(1, 1000)] public int MaxSectionZipGb { get; set; } = 10;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);

    public bool ProxyUploads => string.Equals(UploadMode, "Proxy", StringComparison.OrdinalIgnoreCase);

    /// <summary>True when the granted scopes allow creating folders and uploading files.</summary>
    public static bool CanWrite(string? grantedScopes) =>
        (grantedScopes ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(s => s.EndsWith("/auth/drive", StringComparison.Ordinal));
}

public sealed class EmailOptions
{
    public const string Section = "Email";
    /// <summary>"None" (log only) or "Smtp". Other providers can be added behind IEmailSender.</summary>
    public string Provider { get; set; } = "None";
    public string? FromAddress { get; set; }
    public string? FromName { get; set; } = "Vetanoia";
    /// <summary>Where new-inquiry notifications go.</summary>
    public string? NotifyAddress { get; set; }
    public SmtpSettings Smtp { get; set; } = new();

    public sealed class SmtpSettings
    {
        public string? Host { get; set; }
        public int Port { get; set; } = 587;
        public bool EnableSsl { get; set; } = true;
        public string? Username { get; set; }
        public string? Password { get; set; }
    }
}
