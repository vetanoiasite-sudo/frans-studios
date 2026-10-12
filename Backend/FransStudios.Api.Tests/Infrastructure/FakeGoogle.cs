using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.WebUtilities;

namespace FransStudios.Api.Tests.Infrastructure;

public sealed record FakeItem(string Id, string Name, string MimeType, string? ParentId, byte[] Bytes);

/// <summary>An open resumable upload session.</summary>
public sealed class FakeUpload(string name, string mimeType, string parentId, long size, string? origin)
{
    public string Name { get; } = name;
    public string MimeType { get; } = mimeType;
    public string ParentId { get; } = parentId;
    public long Size { get; } = size;
    public string? Origin { get; } = origin;
    public MemoryStream Received { get; } = new();
    public string? FileId { get; set; }
}

/// <summary>In-memory stand-in for the Google OAuth and Drive v3 endpoints the app calls.</summary>
public sealed class FakeDrive
{
    public const string AccessToken = "test-access-token";
    public const string AccountEmail = "studio@example.com";

    /// <summary>A second Google account (a previously connected one). Its items are only visible to it, unless shared.</summary>
    public const string OldAccessToken = "old-access-token";
    public const string OldRefreshToken = "old-refresh-token";
    public const string OldAccountEmail = "old@example.com";

    /// <summary>A previous account whose access Google no longer accepts (expired, or removed by its owner).</summary>
    public const string DeadRefreshToken = "dead-refresh-token";
    public const string DeadAccountEmail = "gone@example.com";

    /// <summary>
    /// Third account (connected by a test to stand for "the studio switched again"). Same files as the main
    /// account sees, different identity.
    /// </summary>
    public const string ThirdAccountEmail = "third@example.com";

    /// <summary>Owner (access token) of items not owned by the main account.</summary>
    public ConcurrentDictionary<string, string> Owners { get; } = new();
    /// <summary>(item id, email) → permission id.</summary>
    public ConcurrentDictionary<(string ItemId, string Email), string> Shares { get; } = new();
    /// <summary>Copy id → id of the original (the "fsCopyOf" app property).</summary>
    public ConcurrentDictionary<string, string> CopyOf { get; } = new();

    public static string EmailOf(string token) => token == OldAccessToken ? OldAccountEmail : AccountEmail;

    public string OwnerOf(string id) => Owners.TryGetValue(id, out var o) ? o : AccessToken;

    /// <summary>Owned by the caller, or the item or one of its folders is shared with the caller's account.</summary>
    public bool CanAccess(string id, string token)
    {
        if (id == "root") return true;
        for (var current = id; current is not null && current != "root" && Items.TryGetValue(current, out var item); current = item.ParentId)
        {
            if (current == id && OwnerOf(id) == token) return true;
            if (Shares.ContainsKey((current, EmailOf(token)))) return true;
        }
        return false;
    }
    public ConcurrentDictionary<string, FakeItem> Items { get; } = new();
    public ConcurrentDictionary<string, FakeUpload> Uploads { get; } = new();
    public ConcurrentDictionary<string, bool> Trashed { get; } = new();
    public ConcurrentDictionary<string, bool> SharedWithMe { get; } = new();
    public int MediaRequests;
    private int _nextId;

    public string NewId(string prefix) => $"{prefix}{Interlocked.Increment(ref _nextId):D10}";

    public FakeItem Folder(string id, string name, string? parent = null) =>
        Items[id] = new FakeItem(id, name, "application/vnd.google-apps.folder", parent, []);

    public FakeItem File(string id, string name, string mime, string parent, int size = 1000)
    {
        var bytes = new byte[size];
        for (var i = 0; i < size; i++) bytes[i] = (byte)(i % 251);
        return Items[id] = new FakeItem(id, name, mime, parent, bytes);
    }
}

public sealed partial class FakeGoogleHandler(FakeDrive drive) : HttpMessageHandler
{
    private static readonly byte[] TinyJpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10, 0x4A, 0x46, 0x49, 0x46, 0, 1];

    [GeneratedRegex(@"'([^']+)' in parents")]
    private static partial Regex ParentQuery();

    [GeneratedRegex(@"name = '((?:[^'\\]|\\.)*)'")]
    private static partial Regex NameQuery();

    [GeneratedRegex(@"name contains '((?:[^'\\]|\\.)*)'")]
    private static partial Regex ContainsQuery();

    [GeneratedRegex(@"key='fsCopyOf' and value='([^']+)'")]
    private static partial Regex CopyOfQuery();

    private async Task<HttpResponseMessage> StartUploadAsync(HttpRequestMessage request, CancellationToken ct)
    {
        using var meta = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
        var parentId = meta.RootElement.GetProperty("parents")[0].GetString()!;
        if (!drive.Items.ContainsKey(parentId)) return NotFound();
        var size = long.Parse(request.Headers.GetValues("X-Upload-Content-Length").Single());
        var mime = request.Headers.GetValues("X-Upload-Content-Type").Single();
        var origin = request.Headers.TryGetValues("Origin", out var o) ? o.Single() : null;
        var uploadId = drive.NewId("upl");
        drive.Uploads[uploadId] = new FakeUpload(meta.RootElement.GetProperty("name").GetString()!, mime, parentId, size, origin);
        var resp = new HttpResponseMessage(HttpStatusCode.OK);
        resp.Headers.Location = new Uri($"https://www.googleapis.com/upload/drive/v3/files?uploadType=resumable&upload_id={uploadId}");
        return resp;
    }

    /// <summary>"Content-Range: bytes a-b/total" appends a chunk; "bytes */total" only asks for the status.</summary>
    private async Task<HttpResponseMessage> UploadChunkAsync(HttpRequestMessage request, string uploadId, CancellationToken ct)
    {
        if (!drive.Uploads.TryGetValue(uploadId, out var up)) return NotFound();
        var range = request.Content?.Headers.TryGetValues("Content-Range", out var v) == true ? v.Single() : "";
        var bytes = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(ct);

        if (!range.StartsWith("bytes */"))
        {
            var span = range["bytes ".Length..range.IndexOf('/')].Split('-');
            var from = long.Parse(span[0]);
            if (up.FileId is null && from == up.Received.Length) up.Received.Write(bytes); // a repeated chunk is ignored
        }

        if (up.FileId is null && up.Received.Length == up.Size)
        {
            var file = new FakeItem(drive.NewId("fakeFile"), up.Name, up.MimeType, up.ParentId, up.Received.ToArray());
            drive.Items[file.Id] = file;
            up.FileId = file.Id;
        }
        if (up.FileId is not null) return Json(ToJson(drive.Items[up.FileId]), HttpStatusCode.OK);

        var incomplete = new HttpResponseMessage((HttpStatusCode)308);
        if (up.Received.Length > 0) incomplete.Headers.TryAddWithoutValidation("Range", $"bytes=0-{up.Received.Length - 1}");
        return incomplete;
    }

    private static HttpResponseMessage NotFound() =>
        Json(new { error = new { code = 404, message = "File not found", errors = new[] { new { reason = "notFound" } } } }, HttpStatusCode.NotFound);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var uri = request.RequestUri!;

        if (uri.Host == "oauth2.googleapis.com" && uri.AbsolutePath == "/token")
        {
            var form = await request.Content!.ReadAsStringAsync(ct);
            if (form.Contains($"refresh_token={FakeDrive.DeadRefreshToken}"))
                return Json(new { error = "invalid_grant", error_description = "Token has been expired or revoked." }, HttpStatusCode.BadRequest);
            var old = form.Contains($"refresh_token={FakeDrive.OldRefreshToken}");
            return Json(new { access_token = old ? FakeDrive.OldAccessToken : FakeDrive.AccessToken, refresh_token = "fake-refresh-token", expires_in = 3600, scope = "https://www.googleapis.com/auth/drive" });
        }

        // Bytes sent to a resumable session URL: the session URL itself is the authorisation (no token).
        if (request.Method == HttpMethod.Put && uri.AbsolutePath == "/upload/drive/v3/files")
            return await UploadChunkAsync(request, QueryHelpers.ParseQuery(uri.Query)["upload_id"].ToString(), ct);

        var token = request.Headers.Authorization?.Parameter;
        if (token is not (FakeDrive.AccessToken or FakeDrive.OldAccessToken))
            return new HttpResponseMessage(HttpStatusCode.Unauthorized);

        if (uri.Host == "lh3.googleusercontent.com")
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(TinyJpeg) };

        if (request.Method == HttpMethod.Post && uri.AbsolutePath == "/upload/drive/v3/files")
            return await StartUploadAsync(request, ct);

        if (uri.Host != "www.googleapis.com" || !uri.AbsolutePath.StartsWith("/drive/v3/"))
            return new HttpResponseMessage(HttpStatusCode.NotFound);

        var parsed = QueryHelpers.ParseQuery(uri.Query);
        var query = new Func<string, string>(k => parsed.TryGetValue(k, out var v) ? v.ToString() : "");
        var path = uri.AbsolutePath["/drive/v3/".Length..];

        if (path == "about")
            return Json(new
            {
                user = new { emailAddress = FakeDrive.EmailOf(token) },
                storageQuota = new { limit = "16106127360", usage = "5368709120", usageInDrive = "4294967296", usageInDriveTrash = "1073741824" },
            });

        if (path == "files" && request.Method == HttpMethod.Post)
        {
            // Create a folder: {name, mimeType, parents:[id]}
            using var meta = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            var parentId = meta.RootElement.GetProperty("parents")[0].GetString()!;
            if (parentId != "root" && (!drive.Items.ContainsKey(parentId) || !drive.CanAccess(parentId, token))) return NotFound();
            var folder = drive.Folder(drive.NewId("fakeFolder"), meta.RootElement.GetProperty("name").GetString()!, parentId);
            if (token != FakeDrive.AccessToken) drive.Owners[folder.Id] = token;
            return Json(ToJson(folder));
        }

        // Share: POST files/{id}/permissions   Unshare: DELETE files/{id}/permissions/{permissionId}
        if (path.StartsWith("files/") && path.Contains("/permissions"))
        {
            var id = path["files/".Length..path.IndexOf("/permissions")];
            if (!drive.Items.ContainsKey(id) || drive.OwnerOf(id) != token) return NotFound();
            if (request.Method == HttpMethod.Post)
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                var email = body.RootElement.GetProperty("emailAddress").GetString()!;
                var permissionId = drive.Shares.GetOrAdd((id, email), _ => drive.NewId("perm"));
                return Json(new { id = permissionId });
            }
            var removeId = path[(path.LastIndexOf('/') + 1)..];
            foreach (var share in drive.Shares.Where(x => x.Key.ItemId == id && x.Value == removeId).ToList()) drive.Shares.TryRemove(share.Key, out _);
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }

        // Copy: POST files/{id}/copy {name, parents, appProperties}; the copy belongs to the caller.
        if (path.StartsWith("files/") && path.EndsWith("/copy") && request.Method == HttpMethod.Post)
        {
            var id = path["files/".Length..^"/copy".Length];
            if (!drive.Items.TryGetValue(id, out var original) || !drive.CanAccess(id, token)) return NotFound();
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            var parentId = body.RootElement.GetProperty("parents")[0].GetString()!;
            if (!drive.Items.ContainsKey(parentId) || !drive.CanAccess(parentId, token)) return NotFound();
            var copy = original with { Id = drive.NewId("fakeCopy"), Name = body.RootElement.GetProperty("name").GetString()!, ParentId = parentId };
            drive.Items[copy.Id] = copy;
            if (token != FakeDrive.AccessToken) drive.Owners[copy.Id] = token;
            if (body.RootElement.TryGetProperty("appProperties", out var props) && props.TryGetProperty("fsCopyOf", out var of))
                drive.CopyOf[copy.Id] = of.GetString()!;
            return Json(ToJson(copy));
        }

        if (path == "files")
        {
            var q = query("q");
            var parent = ParentQuery().Match(q) is { Success: true } m ? m.Groups[1].Value : null;
            var name = NameQuery().Match(q) is { Success: true } n ? n.Groups[1].Value.Replace("\\'", "'") : null;
            var contains = ContainsQuery().Match(q) is { Success: true } c ? c.Groups[1].Value.Replace("\\'", "'") : null;
            var shared = q.Contains("sharedWithMe = true");
            var copyOf = CopyOfQuery().Match(q) is { Success: true } co ? co.Groups[1].Value : null;
            var foldersOnly = q.Contains("mimeType = 'application/vnd.google-apps.folder'");
            var files = drive.Items.Values
                .Where(i => !drive.Trashed.ContainsKey(i.Id))
                .Where(i => contains is not null ? i.Name.Contains(contains, StringComparison.OrdinalIgnoreCase)
                    : shared ? drive.SharedWithMe.ContainsKey(i.Id)
                    : i.ParentId == parent)
                .Where(i => (!foldersOnly || i.MimeType.EndsWith("folder")) && (name is null || i.Name == name))
                .Where(i => drive.CanAccess(i.Id, token))
                .Where(i => copyOf is null || drive.CopyOf.TryGetValue(i.Id, out var src) && src == copyOf)
                .OrderBy(i => i.MimeType.EndsWith("folder") ? 0 : 1).ThenBy(i => i.Name, StringComparer.Ordinal)
                .Select(ToJson).ToList();
            return Json(new { files });
        }

        if (path.StartsWith("files/") && request.Method == HttpMethod.Patch)
        {
            var id = path["files/".Length..];
            if (!drive.Items.TryGetValue(id, out var item)) return NotFound();
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            if (body.RootElement.TryGetProperty("name", out var newName)) item = item with { Name = newName.GetString()! };
            if (body.RootElement.TryGetProperty("trashed", out var trashed) && trashed.GetBoolean()) drive.Trashed[id] = true;
            if (query("addParents") is { Length: > 0 } to)
            {
                if (to != "root" && !drive.Items.ContainsKey(to)) return NotFound();
                item = item with { ParentId = to };
            }
            drive.Items[id] = item;
            return Json(ToJson(item));
        }

        if (path.StartsWith("files/"))
        {
            var id = path["files/".Length..];
            if (id == "root")
                return Json(ToJson(new FakeItem("root", "My Drive", "application/vnd.google-apps.folder", null, [])));
            if (!drive.Items.TryGetValue(id, out var item) || !drive.CanAccess(id, token))
                return Json(new { error = new { code = 404, message = "File not found", errors = new[] { new { reason = "notFound" } } } }, HttpStatusCode.NotFound);

            if (query("alt") == "media")
            {
                Interlocked.Increment(ref drive.MediaRequests);
                return Media(item.Bytes, request.Headers.Range);
            }
            if (query("fields") == "thumbnailLink")
                return Json(new { thumbnailLink = $"https://lh3.googleusercontent.com/thumb/{id}=s220" });
            return Json(ToJson(item));
        }

        await Task.CompletedTask;
        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private static Dictionary<string, object?> ToJson(FakeItem i) => new()
    {
        ["id"] = i.Id,
        ["name"] = i.Name,
        ["mimeType"] = i.MimeType,
        ["size"] = i.MimeType.EndsWith("folder") ? null : i.Bytes.Length.ToString(),
        ["modifiedTime"] = "2026-09-01T10:00:00.000Z",
        ["hasThumbnail"] = i.MimeType.StartsWith("image/") || i.MimeType.StartsWith("video/"),
        ["trashed"] = false,
        ["webViewLink"] = $"https://drive.google.com/open?id={i.Id}",
        ["capabilities"] = new Dictionary<string, bool>
        {
            ["canRename"] = true, ["canTrash"] = true, ["canMoveItemWithinDrive"] = true, ["canAddChildren"] = i.MimeType.EndsWith("folder"),
        },
        ["parents"] = i.ParentId is null ? Array.Empty<string>() : new[] { i.ParentId },
    };

    private static HttpResponseMessage Media(byte[] bytes, RangeHeaderValue? range)
    {
        if (range is null)
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };

        var r = range.Ranges.Single();
        long from = r.From ?? bytes.Length - r.To!.Value;
        long to = r.From is null ? bytes.Length - 1 : Math.Min(r.To ?? bytes.Length - 1, bytes.Length - 1);
        if (from >= bytes.Length)
            return new HttpResponseMessage(HttpStatusCode.RequestedRangeNotSatisfiable);

        var content = new ByteArrayContent(bytes, (int)from, (int)(to - from + 1));
        content.Headers.ContentRange = new ContentRangeHeaderValue(from, to, bytes.Length);
        return new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = content };
    }

    private static HttpResponseMessage Json(object body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
}
