using System.Net;
using System.Text.Json;
using FransStudios.Api.Data;
using FransStudios.Api.Entities;
using FransStudios.Api.GoogleDrive;
using FransStudios.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FransStudios.Api.Tests;

/// <summary>
/// The studio connects another Google account: albums whose folders are in the previous account's Drive keep
/// working (through that account's stored access), are flagged in the admin, and can be moved to the new Drive.
/// </summary>
public sealed class DriveMoveTests(TestApp app) : IClassFixture<TestApp>
{
    [Fact]
    public async Task Album_on_the_previous_account_keeps_working_and_moves_to_the_connected_drive()
    {
        var albumId = await SeedOldAccountAlbumAsync("oldWedding000001", "Old Wedding");
        var admin = await app.LoginAsync("admin", TestApp.AdminPassword);
        var photo = (await FilesAsync(albumId)).Single(f => f.Name == "IMG_1.jpg");

        // Still served from the old Drive, and flagged as being there.
        var before = await Json(await admin.GetAsync($"/api/admin/albums/{albumId}"));
        Assert.Equal(FakeDrive.OldAccountEmail, before.GetProperty("driveMove").GetProperty("accountEmail").GetString());
        Assert.Equal(JsonValueKind.Null, before.GetProperty("driveMove").GetProperty("state").ValueKind); // not asked to move yet
        var content = await admin.GetAsync($"/api/admin/files/{photo.Id}/content");
        Assert.Equal(HttpStatusCode.OK, content.StatusCode);
        // (Other tests in this class add albums on other accounts too: look at old@example.com only.)
        var other = (await Json(await admin.GetAsync("/api/admin/google-drive/status"))).GetProperty("otherAccounts").EnumerateArray()
            .Single(o => o.GetProperty("email").GetString() == FakeDrive.OldAccountEmail);
        Assert.True(other.GetProperty("available").GetBoolean());
        Assert.True(before.GetProperty("driveMove").GetProperty("available").GetBoolean());

        // Uploading into the old Drive is refused until the album is moved.
        var upload = await admin.SendAsync(HttpMethod.Post, $"/api/admin/albums/{albumId}/uploads",
            new { fileName = "new.jpg", size = 100, mimeType = "image/jpeg", section = "Photos" });
        Assert.Equal(HttpStatusCode.Conflict, upload.StatusCode);

        var started = await admin.SendAsync(HttpMethod.Post, $"/api/admin/albums/{albumId}/move-drive");
        Assert.Equal(HttpStatusCode.OK, started.StatusCode);
        var after = await WaitForMoveAsync(admin, albumId);

        // The album now lives in the connected Drive, in "Frans Studios Albums", with its sub-folders.
        Assert.Equal(JsonValueKind.Null, after.GetProperty("driveMove").ValueKind);
        await using (var scope = app.Services.CreateAsyncScope())
            Assert.False(await scope.ServiceProvider.GetRequiredService<AppDbContext>().AlbumMoves.AnyAsync(m => m.AlbumId == albumId));
        var folder = app.Drive.Items[after.GetProperty("driveFolderId").GetString()!];
        Assert.Equal("Old Wedding", folder.Name);
        Assert.Equal("Frans Studios Albums", app.Drive.Items[folder.ParentId!].Name);
        Assert.Equal(FakeDrive.AccessToken, app.Drive.OwnerOf(folder.Id));
        Assert.Equal("My Drive / Frans Studios Albums / Old Wedding", after.GetProperty("driveFolderName").GetString());

        // Same files (same ids, so links and the cover still work), now copies in the new Drive.
        var files = await FilesAsync(albumId);
        Assert.Equal(["Film.mp4", "IMG_1.jpg", "IMG_2.jpg"], files.Select(f => f.Name).Order());
        var moved = files.Single(f => f.Id == photo.Id);
        Assert.NotEqual(photo.DriveFileId, moved.DriveFileId);
        Assert.Equal(photo.DriveFileId, app.Drive.CopyOf[moved.DriveFileId]);
        Assert.Equal("Photos", app.Drive.Items[app.Drive.Items[moved.DriveFileId].ParentId!].Name);
        Assert.All(files, f => Assert.Null(f.MovedDriveFileId));
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/api/admin/files/{photo.Id}/content")).StatusCode);

        // The originals stay in the old Drive, which is no longer shared.
        Assert.True(app.Drive.Items.ContainsKey(photo.DriveFileId));
        Assert.DoesNotContain(app.Drive.Shares.Keys, k => k.ItemId == "oldWedding000001");
        // Nothing left to move.
        Assert.Equal(HttpStatusCode.Conflict, (await admin.SendAsync(HttpMethod.Post, $"/api/admin/albums/{albumId}/move-drive")).StatusCode);
    }

    [Fact]
    public async Task An_interrupted_move_continues_without_copying_twice()
    {
        var albumId = await SeedOldAccountAlbumAsync("oldWedding000002", "Second Wedding");
        var admin = await app.LoginAsync("admin", TestApp.AdminPassword);

        // A previous attempt made the folder and copied one file, then stopped.
        string targetId, firstCopyId;
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var uploads = scope.ServiceProvider.GetRequiredService<DriveUploadService>();
            var drive = scope.ServiceProvider.GetRequiredService<DriveClient>();
            var album = await db.Albums.SingleAsync(a => a.Id == albumId);
            targetId = (await uploads.CreateNewAlbumFolderAsync(drive, album.Title, default)).Id;
            var first = await db.AlbumFiles.Where(f => f.AlbumId == albumId).OrderBy(f => f.SortOrder).FirstAsync();
            app.Drive.Shares[(album.DriveFolderId!, FakeDrive.AccountEmail)] = "permEarlier01";
            firstCopyId = await drive.CopyFileAsync(first.DriveFileId, (await drive.FindChildFolderAsync(targetId, "Photos", default))!, first.Name, default);
            app.Drive.Shares.Clear();
            db.AlbumMoves.Add(new AlbumMove
            {
                AlbumId = albumId, State = AlbumMoveState.Failed, Error = "Google Drive could not be reached.",
                TargetAccountEmail = FakeDrive.AccountEmail, TargetFolderId = targetId,
            });
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.OK, (await admin.SendAsync(HttpMethod.Post, $"/api/admin/albums/{albumId}/move-drive")).StatusCode);
        var after = await WaitForMoveAsync(admin, albumId);

        Assert.Equal(targetId, after.GetProperty("driveFolderId").GetString());
        Assert.Contains(firstCopyId, (await FilesAsync(albumId)).Select(f => f.DriveFileId));
        Assert.Equal(3, app.Drive.CopyOf.Values.Count(src => src.StartsWith("old2")));
    }

    [Fact]
    public async Task A_move_interrupted_while_heading_to_another_account_starts_again_into_the_connected_one()
    {
        // The studio started moving into a third account, then connected the current one before it finished.
        var albumId = await SeedOldAccountAlbumAsync("oldWedding000003", "Third Wedding");
        var admin = await app.LoginAsync("admin", TestApp.AdminPassword);
        var abandoned = app.Drive.Folder("thirdAccFolder01", "Third Wedding", "root");
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.AlbumFiles.Where(f => f.AlbumId == albumId)
                .ExecuteUpdateAsync(u => u.SetProperty(f => f.MovedDriveFileId, "copyInThirdAcc1"));
            db.AlbumMoves.Add(new AlbumMove
            {
                AlbumId = albumId, State = AlbumMoveState.Failed, Error = "Google Drive refused the move.",
                TargetAccountEmail = FakeDrive.ThirdAccountEmail, TargetFolderId = abandoned.Id,
            });
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.OK, (await admin.SendAsync(HttpMethod.Post, $"/api/admin/albums/{albumId}/move-drive")).StatusCode);
        var after = await WaitForMoveAsync(admin, albumId);

        var folderId = after.GetProperty("driveFolderId").GetString()!;
        Assert.NotEqual(abandoned.Id, folderId);
        Assert.Equal("Frans Studios Albums", app.Drive.Items[app.Drive.Items[folderId].ParentId!].Name);
        var files = await FilesAsync(albumId);
        Assert.Equal(3, files.Count);
        Assert.All(files, f => Assert.Equal(folderId, AlbumFolderOf(f.DriveFileId)));
    }

    [Fact]
    public async Task A_queued_move_is_dropped_when_the_studio_switches_back_to_the_albums_account()
    {
        var albumId = await SeedOldAccountAlbumAsync("oldWedding000004", "Fourth Wedding");
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.AlbumMoves.Add(new AlbumMove { AlbumId = albumId, State = AlbumMoveState.Queued });
        await db.SaveChangesAsync();
        try
        {
            await ConnectAsync(db, FakeDrive.OldAccountEmail); // the old account is the connected one again

            await scope.ServiceProvider.GetRequiredService<DriveMoveService>().RunAsync(albumId, default);

            Assert.False(await db.AlbumMoves.AnyAsync(m => m.AlbumId == albumId));
            Assert.Equal("oldWedding000004", (await db.Albums.AsNoTracking().SingleAsync(a => a.Id == albumId)).DriveFolderId);
            Assert.DoesNotContain(app.Drive.CopyOf.Values, src => src.StartsWith("old4"));
        }
        finally { await ConnectAsync(db, FakeDrive.AccountEmail); }
    }

    /// <summary>Makes the account's latest connection the connected one (only one can be at a time).</summary>
    private static async Task ConnectAsync(AppDbContext db, string email)
    {
        var id = await db.GoogleDriveConnections.Where(c => c.AccountEmail == email).OrderByDescending(c => c.Id).Select(c => c.Id).FirstAsync();
        await db.GoogleDriveConnections.Where(c => c.RevokedAt == null && c.Id != id)
            .ExecuteUpdateAsync(u => u.SetProperty(c => c.RevokedAt, DateTime.UtcNow));
        await db.GoogleDriveConnections.Where(c => c.Id == id).ExecuteUpdateAsync(u => u.SetProperty(c => c.RevokedAt, (DateTime?)null));
    }

    [Fact]
    public async Task An_old_account_whose_access_expired_is_shown_as_unreachable()
    {
        int albumId;
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var protector = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>().CreateProtector("FransStudios.GoogleDrive.RefreshToken.v1");
            db.GoogleDriveConnections.Add(new GoogleDriveConnection
            {
                AccountEmail = FakeDrive.DeadAccountEmail,
                EncryptedRefreshToken = protector.Protect(FakeDrive.DeadRefreshToken),
                Scopes = "https://www.googleapis.com/auth/drive",
                ConnectedByUserId = (await db.Users.SingleAsync(u => u.Username == "admin")).Id,
                ConnectedAt = DateTime.UtcNow.AddDays(-60),
                RevokedAt = DateTime.UtcNow.AddDays(-50),
            });
            var expired = new Album
            {
                ClientId = app.AliceClientId, Title = "Expired Wedding", Status = AlbumStatus.Published,
                DriveFolderId = "goneFolder000001", DriveAccountEmail = FakeDrive.DeadAccountEmail,
            };
            db.Albums.Add(expired);
            await db.SaveChangesAsync();
            albumId = expired.Id;
        }
        var admin = await app.LoginAsync("admin", TestApp.AdminPassword);

        // The status asks Google, which refuses: the account shows as unreachable, and its access is forgotten.
        var gone = (await Json(await admin.GetAsync("/api/admin/google-drive/status"))).GetProperty("otherAccounts").EnumerateArray()
            .Single(o => o.GetProperty("email").GetString() == FakeDrive.DeadAccountEmail);
        Assert.False(gone.GetProperty("available").GetBoolean());
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal("", (await db.GoogleDriveConnections.SingleAsync(c => c.AccountEmail == FakeDrive.DeadAccountEmail)).EncryptedRefreshToken);
        }

        var album = await Json(await admin.GetAsync($"/api/admin/albums/{albumId}"));
        Assert.False(album.GetProperty("driveMove").GetProperty("available").GetBoolean());
        var listed = (await Json(await admin.GetAsync("/api/admin/albums?pageSize=100"))).GetProperty("items").EnumerateArray()
            .Single(a => a.GetProperty("id").GetInt32() == albumId);
        Assert.False(listed.GetProperty("driveMove").GetProperty("available").GetBoolean());

        // It can't be moved (there's nothing to copy from), and "move all" leaves it out.
        var move = await admin.SendAsync(HttpMethod.Post, $"/api/admin/albums/{albumId}/move-drive");
        Assert.Equal(HttpStatusCode.Conflict, move.StatusCode);
        Assert.Contains("Access to gone@example.com", await move.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, (await admin.SendAsync(HttpMethod.Post, "/api/admin/albums/move-drive")).StatusCode);
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.False(await db.AlbumMoves.AnyAsync(m => m.AlbumId == albumId));
        }
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    /// <summary>The album folder a file sits in (the parent of its Photos / Videos folder).</summary>
    private string AlbumFolderOf(string fileId)
    {
        var section = app.Drive.Items[app.Drive.Items[fileId].ParentId!];
        return section.Name is "Photos" or "Videos" ? section.ParentId! : section.Id;
    }

    /// <summary>
    /// A folder (Photos with 2 images, Videos with 1 film) in the old account's Drive, an album using it, and that
    /// account's stored access (replaced as the connected account, not disconnected).
    /// </summary>
    private async Task<int> SeedOldAccountAlbumAsync(string folderId, string title)
    {
        var p = "old" + folderId[^1];
        var d = app.Drive;
        foreach (var item in new[]
                 {
                     d.Folder(folderId, title, "root"),
                     d.Folder($"{p}Photos00000", "Photos", folderId),
                     d.Folder($"{p}Videos00000", "Videos", folderId),
                     d.File($"{p}Photo000001", "IMG_1.jpg", "image/jpeg", $"{p}Photos00000"),
                     d.File($"{p}Photo000002", "IMG_2.jpg", "image/jpeg", $"{p}Photos00000"),
                     d.File($"{p}Video000001", "Film.mp4", "video/mp4", $"{p}Videos00000", size: 4000),
                 })
            d.Owners[item.Id] = FakeDrive.OldAccessToken;

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (!await db.GoogleDriveConnections.AnyAsync(c => c.AccountEmail == FakeDrive.OldAccountEmail))
        {
            var protector = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>().CreateProtector("FransStudios.GoogleDrive.RefreshToken.v1");
            db.GoogleDriveConnections.Add(new GoogleDriveConnection
            {
                AccountEmail = FakeDrive.OldAccountEmail,
                EncryptedRefreshToken = protector.Protect(FakeDrive.OldRefreshToken),
                Scopes = "https://www.googleapis.com/auth/drive",
                ConnectedByUserId = (await db.Users.SingleAsync(u => u.Username == "admin")).Id,
                ConnectedAt = DateTime.UtcNow.AddDays(-30),
                RevokedAt = DateTime.UtcNow.AddDays(-1),
            });
        }
        var album = new Album
        {
            ClientId = app.AliceClientId, Title = title, Status = AlbumStatus.Published,
            DriveFolderId = folderId, DriveAccountEmail = FakeDrive.OldAccountEmail,
        };
        db.Albums.Add(album);
        await db.SaveChangesAsync();

        var result = await scope.ServiceProvider.GetRequiredService<AlbumSyncService>().SyncAsync(album.Id, default);
        Assert.True(result.Success, result.Error); // read through the old account
        Assert.Equal(3, result.FileCount);
        return album.Id;
    }

    private async Task<List<AlbumFile>> FilesAsync(int albumId)
    {
        await using var scope = app.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().AlbumFiles.AsNoTracking()
            .Where(f => f.AlbumId == albumId && !f.IsRemoved).ToListAsync();
    }

    private static async Task<JsonElement> WaitForMoveAsync(ApiSession admin, int albumId)
    {
        for (var i = 0; i < 100; i++)
        {
            var album = await Json(await admin.GetAsync($"/api/admin/albums/{albumId}"));
            var move = album.GetProperty("driveMove");
            if (move.ValueKind == JsonValueKind.Null) return album;
            Assert.NotEqual("failed", move.GetProperty("state").GetString());
            await Task.Delay(100);
        }
        throw new TimeoutException("The move didn't finish.");
    }

    private static async Task<JsonElement> Json(HttpResponseMessage resp)
    {
        Assert.True(resp.IsSuccessStatusCode, $"{(int)resp.StatusCode}: {await resp.Content.ReadAsStringAsync()}");
        return JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement.Clone();
    }
}
