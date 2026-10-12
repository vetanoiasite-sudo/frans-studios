using FransStudios.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace FransStudios.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options, TimeProvider clock) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Client> Clients => Set<Client>();
    public DbSet<Album> Albums => Set<Album>();
    public DbSet<AlbumFile> AlbumFiles => Set<AlbumFile>();
    public DbSet<AlbumMove> AlbumMoves => Set<AlbumMove>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<GoogleDriveConnection> GoogleDriveConnections => Set<GoogleDriveConnection>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<PublicGallery> PublicGalleries => Set<PublicGallery>();
    public DbSet<PublicImage> PublicImages => Set<PublicImage>();
    public DbSet<PublicFilm> PublicFilms => Set<PublicFilm>();
    public DbSet<Package> Packages => Set<Package>();
    public DbSet<Inquiry> Inquiries => Set<Inquiry>();
    public DbSet<PageCover> PageCovers => Set<PageCover>();
    public DbSet<SiteSetting> SiteSettings => Set<SiteSetting>();

    protected override void OnModelCreating(ModelBuilder b) =>
        b.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

    protected override void ConfigureConventions(ModelConfigurationBuilder c)
    {
        // Every DateTime in this app is UTC. SQL Server's datetime2 has no kind, so restore it on read.
        c.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>().HaveColumnType("datetime2(3)");
        c.Properties<DateTime?>().HaveConversion<UtcDateTimeConverter>().HaveColumnType("datetime2(3)");
        c.Properties<decimal>().HavePrecision(12, 2);
    }

    public override Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        foreach (var e in ChangeTracker.Entries())
        {
            if (e.Entity is ITimestamped t)
            {
                if (e.State == EntityState.Added) { t.CreatedAt = now; t.UpdatedAt = now; }
                else if (e.State == EntityState.Modified) t.UpdatedAt = now;
            }
            if (e.Entity is AlbumFile f)
            {
                if (e.State == EntityState.Added) { f.CreatedAt = now; f.UpdatedAt = now; }
                else if (e.State == EntityState.Modified) f.UpdatedAt = now;
            }
            if (e.State == EntityState.Added)
            {
                switch (e.Entity)
                {
                    case Session s when s.CreatedAt == default: s.CreatedAt = now; break;
                    case AuditLog a when a.CreatedAt == default: a.CreatedAt = now; break;
                    case Inquiry i when i.CreatedAt == default: i.CreatedAt = now; break;
                    case PublicImage p when p.CreatedAt == default: p.CreatedAt = now; break;
                }
            }
        }
        return base.SaveChangesAsync(ct);
    }
}

internal sealed class UtcDateTimeConverter() : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime, DateTime>(
    v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(),
    v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
