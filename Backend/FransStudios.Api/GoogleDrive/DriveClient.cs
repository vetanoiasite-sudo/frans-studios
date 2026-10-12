using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FransStudios.Api.GoogleDrive;

public static class DriveHttp
{
    public const string ApiClient = "google-api";
    public const string MediaClient = "google-media";
    public const string ApiBase = "https://www.googleapis.com/drive/v3/";
    public const string UploadBase = "https://www.googleapis.com/upload/drive/v3/";
    public const string FolderMime = "application/vnd.google-apps.folder";
}

public sealed record DriveFolder(string Id, string Name, DateTime? ModifiedTime);

/// <summary>A file or folder as shown in the admin Drive manager.</summary>
public sealed record DriveEntry(
    string Id,
    string Name,
    string MimeType,
    long? Size,
    DateTime? ModifiedTime,
    bool HasThumbnail,
    string? WebViewLink,
    string[] Parents,
    string? ShortcutTargetId,
    string? ShortcutTargetMimeType,
    int? Width,
    int? Height,
    long? DurationMs,
    bool CanRename,
    bool CanTrash,
    bool CanMove,
    bool CanAddChildren);

public sealed record DriveStorage(long? Limit, long Usage, long UsageInDrive, long UsageInTrash, string? Email);

/// <summary>State of a resumable upload: bytes Google has stored so far, and the file id once complete.</summary>
public sealed record UploadProgress(long Received, string? FileId);

public sealed record DriveItem(
    string Id,
    string Name,
    string MimeType,
    long? Size,
    DateTime? ModifiedTime,
    bool HasThumbnail,
    int? Width,
    int? Height,
    int? Rotation,
    long? DurationMs);

/// <summary>
/// Thin client over the Google Drive v3 REST API (the official API, called directly with HttpClient).
/// Every call authenticates with the studio's server-held token: the connected account's, or with
/// <see cref="ForAccount"/> a previously connected account's. Folder/file ids are validated before
/// being placed into Drive query strings.
/// </summary>
public sealed partial class DriveClient(IHttpClientFactory httpFactory, DriveTokenService tokens, ILogger<DriveClient> logger)
{
    /// <summary>The stored connection this client works as; null for the connected account.</summary>
    private int? Account { get; init; }

    /// <summary>
    /// A client for another stored Google account (a connection id from <see cref="DriveTokenService.AccountIdForAsync"/>),
    /// for albums still in a previously connected account's Drive. Null gives a client for the connected account.
    /// </summary>
    public DriveClient ForAccount(int? accountId) => new(httpFactory, tokens, logger) { Account = accountId };

    // Drive ids are URL-safe base64-ish strings. "root" is the alias for My Drive.
    [GeneratedRegex("^(root|[A-Za-z0-9_-]{10,128})$")]
    private static partial Regex IdPattern();

    public static bool IsValidId(string? id) => id is not null && IdPattern().IsMatch(id);

    /// <summary>Accepts a raw id or a Drive folder/file URL and returns the id, or null.</summary>
    public static string? ExtractId(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        input = input.Trim();
        if (IsValidId(input)) return input;
        var m = Regex.Match(input, @"(?:/folders/|/d/|[?&]id=)([A-Za-z0-9_-]{10,128})");
        return m.Success ? m.Groups[1].Value : null;
    }

    private const string ItemFields =
        "id,name,mimeType,size,modifiedTime,hasThumbnail,trashed,parents," +
        "imageMediaMetadata(width,height,rotation),videoMediaMetadata(width,height,durationMillis)";

    // ── Folder browsing (admin picker) ──────────────────────────────────────

    public async Task<List<DriveFolder>> ListFoldersAsync(string parentId, string? search, CancellationToken ct)
    {
        EnsureId(parentId);
        var q = $"mimeType = '{DriveHttp.FolderMime}' and trashed = false";
        q += string.IsNullOrWhiteSpace(search)
            ? $" and '{parentId}' in parents"
            : $" and name contains '{EscapeQ(search.Trim())}'";

        var result = new List<DriveFolder>();
        string? pageToken = null;
        do
        {
            var url = ApiUrl("files", new()
            {
                ["q"] = q,
                ["orderBy"] = "name_natural",
                ["pageSize"] = "200",
                ["fields"] = "nextPageToken,files(id,name,modifiedTime)",
                ["pageToken"] = pageToken,
            });
            using var doc = await GetJsonAsync(url, ct);
            foreach (var f in doc.RootElement.GetProperty("files").EnumerateArray())
                result.Add(new DriveFolder(f.GetProperty("id").GetString()!, f.GetProperty("name").GetString()!, GetDate(f, "modifiedTime")));
            pageToken = doc.RootElement.TryGetProperty("nextPageToken", out var t) ? t.GetString() : null;
        } while (pageToken is not null && result.Count < 1000);

        return result;
    }

    public async Task<(DriveItem Item, bool Trashed, string[] Parents)> GetItemAsync(string id, CancellationToken ct)
    {
        EnsureId(id);
        using var doc = await GetJsonAsync(ApiUrl($"files/{id}", new() { ["fields"] = ItemFields }), ct);
        var e = doc.RootElement;
        var parents = e.TryGetProperty("parents", out var p) ? p.EnumerateArray().Select(x => x.GetString()!).ToArray() : [];
        return (ToItem(e), e.TryGetProperty("trashed", out var tr) && tr.GetBoolean(), parents);
    }

    /// <summary>Human-readable path like "My Drive / Clients / Sara Ahmed Wedding" (display only).</summary>
    public async Task<string> GetFolderPathAsync(string folderId, CancellationToken ct)
    {
        var names = new List<string>();
        var current = folderId;
        for (var depth = 0; depth < 8 && current is not null; depth++)
        {
            var (item, _, parents) = await GetItemAsync(current, ct);
            names.Add(item.Name);
            current = parents.FirstOrDefault();
        }
        names.Reverse();
        return string.Join(" / ", names);
    }

    // ── Album sync ──────────────────────────────────────────────────────────

    public async Task<List<DriveItem>> ListChildrenAsync(string folderId, CancellationToken ct)
    {
        EnsureId(folderId);
        var result = new List<DriveItem>();
        string? pageToken = null;
        do
        {
            var url = ApiUrl("files", new()
            {
                ["q"] = $"'{folderId}' in parents and trashed = false",
                ["pageSize"] = "1000",
                ["fields"] = $"nextPageToken,files({ItemFields})",
                ["pageToken"] = pageToken,
            });
            using var doc = await GetJsonAsync(url, ct);
            foreach (var f in doc.RootElement.GetProperty("files").EnumerateArray()) result.Add(ToItem(f));
            pageToken = doc.RootElement.TryGetProperty("nextPageToken", out var t) ? t.GetString() : null;
        } while (pageToken is not null);
        return result;
    }

    // ── Drive manager (admin): browse, search, rename, move, trash ──────────

    private const string EntryFields =
        "id,name,mimeType,size,modifiedTime,hasThumbnail,webViewLink,parents,shortcutDetails(targetId,targetMimeType)," +
        "imageMediaMetadata(width,height),videoMediaMetadata(width,height,durationMillis)," +
        "capabilities(canRename,canTrash,canMoveItemWithinDrive,canAddChildren)";

    /// <summary>
    /// One page of a folder's contents (folders first, then by name), of the files shared with the account, or
    /// of a name search across the whole Drive.
    /// </summary>
    public async Task<(List<DriveEntry> Items, string? NextPageToken)> ListEntriesAsync(string? parentId, string? search,
        bool sharedWithMe, string? pageToken, CancellationToken ct)
    {
        string q;
        if (!string.IsNullOrWhiteSpace(search)) q = $"name contains '{EscapeQ(search.Trim())}' and trashed = false";
        else if (sharedWithMe) q = "sharedWithMe = true and trashed = false";
        else
        {
            parentId ??= "root";
            EnsureId(parentId);
            q = $"'{parentId}' in parents and trashed = false";
        }
        var url = ApiUrl("files", new()
        {
            ["q"] = q,
            ["orderBy"] = "folder,name_natural",
            ["pageSize"] = "200",
            ["fields"] = $"nextPageToken,files({EntryFields})",
            ["pageToken"] = pageToken,
        });
        using var doc = await GetJsonAsync(url, ct);
        var items = doc.RootElement.GetProperty("files").EnumerateArray().Select(ToEntry).ToList();
        var next = doc.RootElement.TryGetProperty("nextPageToken", out var t) ? t.GetString() : null;
        return (items, next);
    }

    public async Task<DriveEntry> GetEntryAsync(string id, CancellationToken ct)
    {
        EnsureId(id);
        using var doc = await GetJsonAsync(ApiUrl($"files/{id}", new() { ["fields"] = EntryFields }), ct);
        return ToEntry(doc.RootElement);
    }

    /// <summary>Folder path from My Drive down to <paramref name="folderId"/> (for breadcrumbs).</summary>
    public async Task<List<(string Id, string Name)>> GetTrailAsync(string folderId, CancellationToken ct)
    {
        var trail = new List<(string, string)>();
        var current = folderId;
        for (var depth = 0; depth < 20 && current is not null; depth++)
        {
            var e = await GetEntryAsync(current, ct);
            trail.Add((e.Id, e.Name));
            current = e.Parents.FirstOrDefault();
        }
        trail.Reverse();
        return trail;
    }

    public async Task<DriveStorage> GetStorageAsync(CancellationToken ct)
    {
        using var doc = await GetJsonAsync(DriveHttp.ApiBase + "about?fields=storageQuota,user(emailAddress)", ct);
        var q = doc.RootElement.GetProperty("storageQuota");
        long? L(string name) => q.TryGetProperty(name, out var v) && long.TryParse(v.GetString(), out var n) ? n : null;
        var email = doc.RootElement.TryGetProperty("user", out var u) && u.TryGetProperty("emailAddress", out var e) ? e.GetString() : null;
        return new DriveStorage(L("limit"), L("usage") ?? 0, L("usageInDrive") ?? 0, L("usageInDriveTrash") ?? 0, email);
    }

    public async Task<DriveEntry> RenameAsync(string id, string name, CancellationToken ct)
    {
        EnsureId(id);
        using var doc = await SendJsonAsync(HttpMethod.Patch, ApiUrl($"files/{id}", new() { ["fields"] = EntryFields }), new { name }, ct);
        return ToEntry(doc.RootElement);
    }

    /// <summary>Moves an item into <paramref name="toFolderId"/>, taking it out of its current folders.</summary>
    public async Task<DriveEntry> MoveAsync(string id, string toFolderId, IEnumerable<string> fromFolderIds, CancellationToken ct)
    {
        EnsureId(id);
        EnsureId(toFolderId);
        var url = ApiUrl($"files/{id}", new()
        {
            ["addParents"] = toFolderId,
            ["removeParents"] = string.Join(',', fromFolderIds.Where(p => p != toFolderId)),
            ["fields"] = EntryFields,
        });
        using var doc = await SendJsonAsync(HttpMethod.Patch, url, new { }, ct);
        return ToEntry(doc.RootElement);
    }

    /// <summary>Moves an item to the Drive trash (restorable there for 30 days). Never deletes permanently.</summary>
    public async Task TrashAsync(string id, CancellationToken ct)
    {
        EnsureId(id);
        using var _ = await SendJsonAsync(HttpMethod.Patch, ApiUrl($"files/{id}", new() { ["fields"] = "id" }), new { trashed = true }, ct);
    }

    private static DriveEntry ToEntry(JsonElement f)
    {
        var item = ToItem(f);
        string? shortcutId = null, shortcutMime = null;
        if (f.TryGetProperty("shortcutDetails", out var sc))
        {
            shortcutId = sc.TryGetProperty("targetId", out var tid) ? tid.GetString() : null;
            shortcutMime = sc.TryGetProperty("targetMimeType", out var tm) ? tm.GetString() : null;
        }
        bool Can(string name) => f.TryGetProperty("capabilities", out var c) && c.TryGetProperty(name, out var v) && v.GetBoolean();
        var parents = f.TryGetProperty("parents", out var p) ? p.EnumerateArray().Select(x => x.GetString()!).ToArray() : [];
        return new DriveEntry(item.Id, item.Name, item.MimeType, item.Size, item.ModifiedTime, item.HasThumbnail,
            f.TryGetProperty("webViewLink", out var link) ? link.GetString() : null, parents, shortcutId, shortcutMime,
            item.Width, item.Height, item.DurationMs,
            Can("canRename"), Can("canTrash"), Can("canMoveItemWithinDrive"), Can("canAddChildren"));
    }

    // ── Writing (uploads into the studio's Drive; needs the full "drive" scope) ──

    /// <summary>The id of a direct sub-folder called <paramref name="name"/>, or null.</summary>
    public async Task<string?> FindChildFolderAsync(string parentId, string name, CancellationToken ct)
    {
        EnsureId(parentId);
        var url = ApiUrl("files", new()
        {
            ["q"] = $"mimeType = '{DriveHttp.FolderMime}' and trashed = false and '{parentId}' in parents and name = '{EscapeQ(name)}'",
            ["pageSize"] = "1",
            ["fields"] = "files(id)",
        });
        using var doc = await GetJsonAsync(url, ct);
        var files = doc.RootElement.GetProperty("files");
        return files.GetArrayLength() > 0 ? files[0].GetProperty("id").GetString() : null;
    }

    public async Task<DriveFolder> CreateFolderAsync(string parentId, string name, CancellationToken ct)
    {
        EnsureId(parentId);
        var url = ApiUrl("files", new() { ["fields"] = "id,name,modifiedTime" });
        using var doc = await SendJsonAsync(HttpMethod.Post, url,
            new { name, mimeType = DriveHttp.FolderMime, parents = new[] { parentId } }, ct);
        var e = doc.RootElement;
        return new DriveFolder(e.GetProperty("id").GetString()!, e.GetProperty("name").GetString()!, GetDate(e, "modifiedTime"));
    }

    // ── Moving between accounts ─────────────────────────────────────────────

    /// <summary>Gives <paramref name="email"/> edit access to a file or folder (and so everything inside it), without emailing them.</summary>
    public async Task<string> ShareWithAsync(string id, string email, CancellationToken ct)
    {
        EnsureId(id);
        var url = ApiUrl($"files/{id}/permissions", new() { ["sendNotificationEmail"] = "false", ["fields"] = "id" });
        using var doc = await SendJsonAsync(HttpMethod.Post, url, new { type = "user", role = "writer", emailAddress = email }, ct);
        return doc.RootElement.GetProperty("id").GetString()!;
    }

    public async Task RemovePermissionAsync(string id, string permissionId, CancellationToken ct)
    {
        EnsureId(id);
        if (!IsValidId(permissionId)) throw new ArgumentException("Invalid permission id.", nameof(permissionId));
        using var _ = await SendJsonAsync(HttpMethod.Delete, ApiUrl($"files/{id}/permissions/{permissionId}", new()), null, ct);
    }

    /// <summary>
    /// Copies a file into <paramref name="parentId"/>; the copy belongs to the account making the call. Google copies
    /// the bytes on its side, so nothing passes through this server however big the file is. The copy is tagged with
    /// the original's id, so <see cref="FindCopyAsync"/> can tell whether an interrupted copy already happened.
    /// </summary>
    public async Task<string> CopyFileAsync(string fileId, string parentId, string name, CancellationToken ct)
    {
        EnsureId(fileId);
        EnsureId(parentId);
        // Copying a multi-GB video can take minutes on Google's side: allow longer than the usual API timeout.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(30));
        using var doc = await SendJsonAsync(HttpMethod.Post, ApiUrl($"files/{fileId}/copy", new() { ["fields"] = "id" }),
            new { name, parents = new[] { parentId }, appProperties = new Dictionary<string, string> { [CopyOfProperty] = fileId } },
            timeout.Token, DriveHttp.MediaClient);
        return doc.RootElement.GetProperty("id").GetString()!;
    }

    private const string CopyOfProperty = "fsCopyOf";

    /// <summary>The id of a copy of <paramref name="fileId"/> already in <paramref name="parentId"/>, or null.</summary>
    public async Task<string?> FindCopyAsync(string fileId, string parentId, CancellationToken ct)
    {
        EnsureId(fileId);
        EnsureId(parentId);
        var url = ApiUrl("files", new()
        {
            ["q"] = $"'{parentId}' in parents and trashed = false and appProperties has {{ key='{CopyOfProperty}' and value='{fileId}' }}",
            ["pageSize"] = "1",
            ["fields"] = "files(id)",
        });
        using var doc = await GetJsonAsync(url, ct);
        var files = doc.RootElement.GetProperty("files");
        return files.GetArrayLength() > 0 ? files[0].GetProperty("id").GetString() : null;
    }

    /// <summary>
    /// Starts a resumable upload of one file into <paramref name="parentId"/> and returns Google's session URL.
    /// The session URL authorises uploading that single file only (no access token is needed to send bytes
    /// to it), so it can be handed to the admin's browser. <paramref name="origin"/> lets Google answer the
    /// browser's cross-origin chunk requests.
    /// </summary>
    public async Task<string> StartResumableUploadAsync(string parentId, string name, string mimeType, long size,
        string? origin, CancellationToken ct)
    {
        EnsureId(parentId);
        var url = DriveHttp.UploadBase + "files" + QueryString.Create(new Dictionary<string, string?>
        {
            ["uploadType"] = "resumable",
            ["supportsAllDrives"] = "true",
            ["fields"] = "id,name,size,parents",
        });
        using var http = httpFactory.CreateClient(DriveHttp.ApiClient);
        for (var attempt = 0; ; attempt++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = JsonContent.Create(new { name, parents = new[] { parentId } }),
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokens.GetAccessTokenAsync(Account, ct));
            req.Headers.Add("X-Upload-Content-Type", mimeType);
            req.Headers.Add("X-Upload-Content-Length", size.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (origin is not null) req.Headers.Add("Origin", origin);
            using var resp = await http.SendAsync(req, ct);

            if (resp.IsSuccessStatusCode && resp.Headers.Location is { } location) return location.ToString();
            if (resp.StatusCode == HttpStatusCode.Unauthorized && attempt == 0) { tokens.InvalidateAccessToken(Account); continue; }
            var ex = await ToExceptionAsync(resp, ct);
            if (IsRetryable(resp, ex) && attempt < 4) { await BackoffAsync(attempt, resp, ex, ct); continue; }
            throw ex;
        }
    }

    /// <summary>Asks Google how much of a resumable upload it has stored (an empty PUT with "bytes */size").</summary>
    public Task<UploadProgress> QueryUploadAsync(string sessionUrl, long size, CancellationToken ct) =>
        PutToSessionAsync(sessionUrl, $"bytes */{size}", null, ct);

    /// <summary>Sends one chunk of a resumable upload through the server (when browsers can't reach Google directly).</summary>
    public Task<UploadProgress> PutChunkAsync(string sessionUrl, Stream body, long offset, long length, long size, CancellationToken ct) =>
        PutToSessionAsync(sessionUrl, $"bytes {offset}-{offset + length - 1}/{size}", new StreamContent(body), ct);

    private async Task<UploadProgress> PutToSessionAsync(string sessionUrl, string contentRange, HttpContent? body, CancellationToken ct)
    {
        var http = httpFactory.CreateClient(DriveHttp.MediaClient);
        using var req = new HttpRequestMessage(HttpMethod.Put, sessionUrl) { Content = body ?? new ByteArrayContent([]) };
        req.Content.Headers.TryAddWithoutValidation("Content-Range", contentRange);
        using var resp = await http.SendAsync(req, ct);

        // 308 "Resume Incomplete": the Range header (if any) says which bytes Google has, e.g. "bytes=0-1048575".
        if ((int)resp.StatusCode == 308)
        {
            var received = 0L;
            if (resp.Headers.TryGetValues("Range", out var values)
                && values.FirstOrDefault() is { } range
                && long.TryParse(range[(range.LastIndexOf('-') + 1)..], out var last))
                received = last + 1;
            return new UploadProgress(received, null);
        }
        if (resp.IsSuccessStatusCode)
        {
            using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var e = doc.RootElement;
            var size = e.TryGetProperty("size", out var s) && long.TryParse(s.GetString(), out var n) ? n : 0;
            return new UploadProgress(size, e.GetProperty("id").GetString());
        }
        throw await ToExceptionAsync(resp, ct);
    }

    // ── Media ───────────────────────────────────────────────────────────────

    /// <summary>Downloads a Drive thumbnail at the requested pixel size (longest edge). Null if none exists.</summary>
    public async Task<byte[]?> GetThumbnailAsync(string fileId, int size, CancellationToken ct)
    {
        EnsureId(fileId);
        using var doc = await GetJsonAsync(ApiUrl($"files/{fileId}", new() { ["fields"] = "thumbnailLink" }), ct);
        if (!doc.RootElement.TryGetProperty("thumbnailLink", out var link) || link.GetString() is not { } thumbUrl) return null;

        // thumbnailLink ends with a size directive like "=s220"; ask for the size we want.
        thumbUrl = Regex.Replace(thumbUrl, @"=s\d+(-[a-z0-9-]+)?$", $"=s{size}");
        if (!thumbUrl.Contains("=s")) thumbUrl += $"=s{size}";

        using var http = httpFactory.CreateClient(DriveHttp.ApiClient);
        using var req = new HttpRequestMessage(HttpMethod.Get, thumbUrl);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokens.GetAccessTokenAsync(Account, ct));
        using var resp = await http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            logger.LogInformation("Thumbnail fetch for {FileId} returned {Status}", fileId, (int)resp.StatusCode);
            return null;
        }
        return await resp.Content.ReadAsByteArrayAsync(ct);
    }

    /// <summary>
    /// Opens the file's bytes from Drive, forwarding an optional Range header. The response is returned
    /// with headers read but the body unread, so callers can stream it without buffering.
    /// The caller owns (and must dispose) the returned response.
    /// </summary>
    public async Task<HttpResponseMessage> OpenMediaAsync(string fileId, RangeHeaderValue? range, CancellationToken ct)
    {
        EnsureId(fileId);
        var http = httpFactory.CreateClient(DriveHttp.MediaClient);
        for (var attempt = 0; ; attempt++)
        {
            var req = new HttpRequestMessage(HttpMethod.Get,
                ApiUrl($"files/{fileId}", new() { ["alt"] = "media" }));
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokens.GetAccessTokenAsync(Account, ct));
            req.Headers.Range = range;
            var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            if (resp.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
            {
                resp.Dispose();
                tokens.InvalidateAccessToken(Account);
                continue;
            }
            return resp;
        }
    }

    public static async Task<DriveApiException> ToExceptionAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        var reason = "unknown";
        var message = resp.ReasonPhrase ?? "Drive request failed.";
        try
        {
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            if (doc.RootElement.TryGetProperty("error", out var err))
            {
                if (err.TryGetProperty("message", out var m)) message = m.GetString() ?? message;
                if (err.TryGetProperty("errors", out var errs) && errs.GetArrayLength() > 0
                    && errs[0].TryGetProperty("reason", out var r)) reason = r.GetString() ?? reason;
            }
        }
        catch (JsonException) { }
        return new DriveApiException((int)resp.StatusCode, reason, message);
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private Task<JsonDocument> GetJsonAsync(string url, CancellationToken ct) => SendJsonAsync(HttpMethod.Get, url, null, ct);

    private async Task<JsonDocument> SendJsonAsync(HttpMethod method, string url, object? body, CancellationToken ct,
        string client = DriveHttp.ApiClient)
    {
        using var http = httpFactory.CreateClient(client);
        for (var attempt = 0; ; attempt++)
        {
            using var req = new HttpRequestMessage(method, url);
            if (body is not null) req.Content = JsonContent.Create(body);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokens.GetAccessTokenAsync(Account, ct));
            using var resp = await http.SendAsync(req, ct);

            if (resp.StatusCode == HttpStatusCode.NoContent) return JsonDocument.Parse("{}");
            if (resp.IsSuccessStatusCode)
                return await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);

            if (resp.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
            {
                tokens.InvalidateAccessToken(Account);
                continue;
            }

            var ex = await ToExceptionAsync(resp, ct);
            if (IsRetryable(resp, ex) && attempt < 4) { await BackoffAsync(attempt, resp, ex, ct); continue; }
            throw ex;
        }
    }

    private static bool IsRetryable(HttpResponseMessage resp, DriveApiException ex) =>
        resp.StatusCode is HttpStatusCode.TooManyRequests or >= HttpStatusCode.InternalServerError
        || ex.Reason is "userRateLimitExceeded" or "rateLimitExceeded";

    /// <summary>Exponential backoff with jitter, as recommended by the Drive API docs.</summary>
    private async Task BackoffAsync(int attempt, HttpResponseMessage resp, DriveApiException ex, CancellationToken ct)
    {
        var delay = TimeSpan.FromMilliseconds(Math.Pow(2, attempt) * 500 + Random.Shared.Next(0, 250));
        logger.LogInformation("Drive {Status}/{Reason}; retrying in {Delay}", (int)resp.StatusCode, ex.Reason, delay);
        await Task.Delay(delay, ct);
    }

    private static string ApiUrl(string path, Dictionary<string, string?> query)
    {
        query["supportsAllDrives"] = "true";              // Workspace shared drives work too
        if (path == "files" && !query.ContainsKey("alt")) query["includeItemsFromAllDrives"] = "true";
        return DriveHttp.ApiBase + path + QueryString.Create(query.Where(kv => kv.Value is not null));
    }

    private static void EnsureId(string id)
    {
        if (!IsValidId(id)) throw new ArgumentException("Invalid Google Drive id.", nameof(id));
    }

    /// <summary>Escapes a value for use inside a single-quoted Drive query string.</summary>
    private static string EscapeQ(string value) => value.Replace("\\", "\\\\").Replace("'", "\\'");

    private static DriveItem ToItem(JsonElement f)
    {
        int? width = null, height = null, rotation = null;
        long? duration = null;
        if (f.TryGetProperty("imageMediaMetadata", out var img))
        {
            width = GetInt(img, "width"); height = GetInt(img, "height"); rotation = GetInt(img, "rotation");
        }
        if (f.TryGetProperty("videoMediaMetadata", out var vid))
        {
            width = GetInt(vid, "width"); height = GetInt(vid, "height");
            duration = vid.TryGetProperty("durationMillis", out var d) && long.TryParse(d.GetString(), out var ms) ? ms : null;
        }
        return new DriveItem(
            f.GetProperty("id").GetString()!,
            f.GetProperty("name").GetString()!,
            f.GetProperty("mimeType").GetString()!,
            f.TryGetProperty("size", out var s) && long.TryParse(s.GetString(), out var size) ? size : null,
            GetDate(f, "modifiedTime"),
            f.TryGetProperty("hasThumbnail", out var ht) && ht.GetBoolean(),
            width, height, rotation, duration);
    }

    private static int? GetInt(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;

    private static DateTime? GetDate(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.TryGetDateTime(out var d) ? d.ToUniversalTime() : null;
}
