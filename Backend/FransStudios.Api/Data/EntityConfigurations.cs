using FransStudios.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FransStudios.Api.Data;

// All relationships use NO ACTION: rows are soft-deleted (or deleted explicitly by services),
// so the database never silently cascades away client data.

internal sealed class UserConfig : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> e)
    {
        e.Property(x => x.Username).HasMaxLength(64).IsRequired();
        e.Property(x => x.PasswordHash).HasMaxLength(256).IsRequired();
        e.Property(x => x.Role).HasConversion<byte>();
        e.HasIndex(x => x.Username).IsUnique().HasFilter("[DeletedAt] IS NULL");
        e.HasIndex(x => x.ClientId);
        e.HasOne(x => x.Client).WithMany(c => c.Users).HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.NoAction);
        e.ToTable(t => t.HasCheckConstraint("CK_Users_RoleClient",
            "([Role] = 1 AND [ClientId] IS NULL) OR ([Role] = 2 AND [ClientId] IS NOT NULL)"));
        e.HasQueryFilter(x => x.DeletedAt == null);
    }
}

internal sealed class ClientConfig : IEntityTypeConfiguration<Client>
{
    public void Configure(EntityTypeBuilder<Client> e)
    {
        e.Property(x => x.FullName).HasMaxLength(200).IsRequired();
        e.Property(x => x.Email).HasMaxLength(256);
        e.Property(x => x.Phone).HasMaxLength(32);
        e.Property(x => x.Notes).HasMaxLength(4000);
        e.HasIndex(x => x.FullName);
        e.HasQueryFilter(x => x.DeletedAt == null);
    }
}

internal sealed class AlbumConfig : IEntityTypeConfiguration<Album>
{
    public void Configure(EntityTypeBuilder<Album> e)
    {
        e.Property(x => x.Title).HasMaxLength(200).IsRequired();
        e.Property(x => x.Description).HasMaxLength(4000);
        e.Property(x => x.Status).HasConversion<byte>();
        e.Property(x => x.DriveFolderId).HasMaxLength(128).IsUnicode(false);
        e.Property(x => x.DriveFolderName).HasMaxLength(400);
        e.Property(x => x.LastSyncError).HasMaxLength(1000);
        e.Property(x => x.DriveAccountEmail).HasMaxLength(320);
        e.HasIndex(x => x.PublicId).IsUnique();
        e.HasIndex(x => new { x.ClientId, x.Status });
        e.HasOne(x => x.Client).WithMany(c => c.Albums).HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.NoAction);
        e.HasOne(x => x.CoverFile).WithMany().HasForeignKey(x => x.CoverFileId).OnDelete(DeleteBehavior.NoAction);
        e.HasQueryFilter(x => x.DeletedAt == null);
    }
}

internal sealed class AlbumFileConfig : IEntityTypeConfiguration<AlbumFile>
{
    public void Configure(EntityTypeBuilder<AlbumFile> e)
    {
        e.Property(x => x.DriveFileId).HasMaxLength(128).IsUnicode(false).IsRequired();
        e.Property(x => x.MovedDriveFileId).HasMaxLength(128).IsUnicode(false);
        e.Property(x => x.Name).HasMaxLength(400).IsRequired();
        e.Property(x => x.Section).HasMaxLength(400).IsRequired();
        e.Property(x => x.MimeType).HasMaxLength(128).IsRequired();
        e.Property(x => x.Kind).HasConversion<byte>();
        // Random GUID keys make poor clustered indexes; cluster on the gallery read order instead.
        e.HasKey(x => x.Id).IsClustered(false);
        e.HasIndex(x => new { x.AlbumId, x.IsRemoved, x.SortOrder }).IsClustered();
        e.HasIndex(x => new { x.AlbumId, x.DriveFileId }).IsUnique();
        e.HasOne(x => x.Album).WithMany(a => a.Files).HasForeignKey(x => x.AlbumId).OnDelete(DeleteBehavior.NoAction);
        // Matches the Album filter so files of a soft-deleted album are invisible too.
        e.HasQueryFilter(x => x.Album.DeletedAt == null);
    }
}

internal sealed class AlbumMoveConfig : IEntityTypeConfiguration<AlbumMove>
{
    public void Configure(EntityTypeBuilder<AlbumMove> e)
    {
        e.HasKey(x => x.AlbumId);
        e.Property(x => x.State).HasConversion<byte>();
        e.Property(x => x.TargetAccountEmail).HasMaxLength(320);
        e.Property(x => x.TargetFolderId).HasMaxLength(128).IsUnicode(false);
        e.Property(x => x.Error).HasMaxLength(1000);
        e.HasOne(x => x.Album).WithOne(a => a.Move).HasForeignKey<AlbumMove>(x => x.AlbumId).OnDelete(DeleteBehavior.Cascade);
        // Matches the Album filter, like AlbumFile.
        e.HasQueryFilter(x => x.Album.DeletedAt == null);
    }
}

internal sealed class SessionConfig : IEntityTypeConfiguration<Session>
{
    public void Configure(EntityTypeBuilder<Session> e)
    {
        e.Property(x => x.IpAddress).HasMaxLength(45).IsUnicode(false);
        e.Property(x => x.UserAgent).HasMaxLength(400);
        e.HasKey(x => x.Id).IsClustered(false);
        e.HasIndex(x => new { x.UserId, x.RevokedAt });
        e.HasOne(x => x.User).WithMany(u => u.Sessions).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.NoAction);
        e.HasQueryFilter(x => x.User.DeletedAt == null);
    }
}

internal sealed class GoogleDriveConnectionConfig : IEntityTypeConfiguration<GoogleDriveConnection>
{
    public void Configure(EntityTypeBuilder<GoogleDriveConnection> e)
    {
        e.Property(x => x.AccountEmail).HasMaxLength(256).IsRequired();
        e.Property(x => x.EncryptedRefreshToken).IsRequired();
        e.Property(x => x.Scopes).HasMaxLength(1000).IsRequired();
        e.HasOne(x => x.ConnectedByUser).WithMany().HasForeignKey(x => x.ConnectedByUserId).OnDelete(DeleteBehavior.NoAction);
        // Only one active connection at a time.
        e.HasIndex(x => x.RevokedAt).IsUnique().HasFilter("[RevokedAt] IS NULL");
    }
}

internal sealed class AuditLogConfig : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> e)
    {
        e.Property(x => x.Action).HasMaxLength(64).IsUnicode(false).IsRequired();
        e.Property(x => x.EntityType).HasMaxLength(64).IsUnicode(false);
        e.Property(x => x.EntityId).HasMaxLength(64).IsUnicode(false);
        e.Property(x => x.IpAddress).HasMaxLength(45).IsUnicode(false);
        e.Property(x => x.Details).HasMaxLength(4000);
        e.HasIndex(x => x.CreatedAt);
        e.HasIndex(x => new { x.UserId, x.CreatedAt });
        e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.NoAction);
    }
}

internal static class ImageAssetMapping
{
    public static void Map<T>(OwnedNavigationBuilder<T, ImageAsset> o) where T : class
    {
        o.Property(x => x.Key).HasMaxLength(200).IsUnicode(false).IsRequired();
        o.Property(x => x.Widths).HasMaxLength(100).IsUnicode(false).IsRequired();
    }
}

internal sealed class PublicGalleryConfig : IEntityTypeConfiguration<PublicGallery>
{
    public void Configure(EntityTypeBuilder<PublicGallery> e)
    {
        e.Property(x => x.Title).HasMaxLength(200).IsRequired();
        e.Property(x => x.Slug).HasMaxLength(120).IsUnicode(false).IsRequired();
        e.Property(x => x.Category).HasConversion<byte>();
        e.Property(x => x.Location).HasMaxLength(200);
        e.Property(x => x.Intro).HasMaxLength(4000);
        e.Property(x => x.SeoDescription).HasMaxLength(300);
        e.HasIndex(x => x.Slug).IsUnique();
        e.HasIndex(x => new { x.IsPublished, x.SortOrder });
        e.HasOne(x => x.CoverImage).WithMany().HasForeignKey(x => x.CoverImageId).OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class PublicImageConfig : IEntityTypeConfiguration<PublicImage>
{
    public void Configure(EntityTypeBuilder<PublicImage> e)
    {
        e.OwnsOne(x => x.Image, ImageAssetMapping.Map);
        e.Navigation(x => x.Image).IsRequired();
        e.Property(x => x.AltText).HasMaxLength(300).IsRequired();
        e.HasIndex(x => new { x.GalleryId, x.SortOrder });
        e.HasOne(x => x.Gallery).WithMany(g => g.Images).HasForeignKey(x => x.GalleryId).OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class PublicFilmConfig : IEntityTypeConfiguration<PublicFilm>
{
    public void Configure(EntityTypeBuilder<PublicFilm> e)
    {
        e.Property(x => x.Title).HasMaxLength(200).IsRequired();
        e.Property(x => x.Description).HasMaxLength(2000);
        e.OwnsOne(x => x.Poster, ImageAssetMapping.Map);
        e.Property(x => x.VideoKey).HasMaxLength(200).IsUnicode(false);
        e.Property(x => x.VideoMimeType).HasMaxLength(64).IsUnicode(false);
        e.HasIndex(x => new { x.IsPublished, x.SortOrder });
    }
}

internal sealed class PackageConfig : IEntityTypeConfiguration<Package>
{
    public void Configure(EntityTypeBuilder<Package> e)
    {
        e.Property(x => x.Name).HasMaxLength(120).IsRequired();
        e.Property(x => x.Summary).HasMaxLength(1000);
        e.Property(x => x.Currency).HasMaxLength(3).IsUnicode(false);
        e.Property(x => x.PriceNote).HasMaxLength(120);
        e.Property(x => x.Features).HasMaxLength(4000);
        e.HasIndex(x => new { x.IsPublished, x.SortOrder });
    }
}

internal sealed class InquiryConfig : IEntityTypeConfiguration<Inquiry>
{
    public void Configure(EntityTypeBuilder<Inquiry> e)
    {
        e.Property(x => x.Name).HasMaxLength(200).IsRequired();
        e.Property(x => x.Mobile).HasMaxLength(32).IsRequired();
        e.Property(x => x.Email).HasMaxLength(256);
        e.Property(x => x.EventType).HasMaxLength(64);
        e.Property(x => x.Venue).HasMaxLength(200);
        e.Property(x => x.Coverage).HasMaxLength(32);
        e.Property(x => x.Source).HasMaxLength(64);
        e.Property(x => x.Message).HasMaxLength(4000);
        e.Property(x => x.Status).HasConversion<byte>();
        e.Property(x => x.IpAddress).HasMaxLength(45).IsUnicode(false);
        e.HasIndex(x => new { x.Status, x.CreatedAt });
    }
}

internal sealed class PageCoverConfig : IEntityTypeConfiguration<PageCover>
{
    public void Configure(EntityTypeBuilder<PageCover> e)
    {
        e.HasKey(x => x.Page);
        e.Property(x => x.Page).HasMaxLength(32).IsUnicode(false);
        e.OwnsOne(x => x.Image, ImageAssetMapping.Map);
        e.Navigation(x => x.Image).IsRequired();
    }
}

internal sealed class SiteSettingConfig : IEntityTypeConfiguration<SiteSetting>
{
    public void Configure(EntityTypeBuilder<SiteSetting> e)
    {
        e.HasKey(x => x.Key);
        e.Property(x => x.Key).HasMaxLength(64).IsUnicode(false);
        e.Property(x => x.Value).HasMaxLength(1000);
    }
}
