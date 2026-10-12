using System.ComponentModel.DataAnnotations;
using FransStudios.Api.Authentication;
using FransStudios.Api.Data;
using FransStudios.Api.Entities;
using FransStudios.Api.Features.Auth;
using FransStudios.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FransStudios.Api.Features.Admin;

public sealed record ClientListItem(int Id, string FullName, string Username, string? Email, string? Phone,
    bool IsActive, int AlbumCount, DateTime? LastLoginAt, DateTime CreatedAt);

public sealed record ClientAlbumSummary(int Id, string Title, AlbumStatus Status, int FileCount, long TotalBytes, DateOnly? EventDate);

public sealed record ActivityItem(string Action, DateTime CreatedAt, string? IpAddress, string? Details);

public sealed record ClientDetail(int Id, string FullName, string Username, string? Email, string? Phone, string? Notes,
    bool IsActive, DateTime? LastLoginAt, DateTime CreatedAt, IReadOnlyList<ClientAlbumSummary> Albums,
    IReadOnlyList<ActivityItem> RecentActivity);

public sealed record CreateClientRequest(
    [Required, StringLength(200, MinimumLength = 2)] string FullName,
    [Required, RegularExpression(UsernameRules.Pattern, ErrorMessage = UsernameRules.Message)] string Username,
    [EmailAddress, StringLength(256)] string? Email,
    [StringLength(32)] string? Phone,
    [StringLength(4000)] string? Notes,
    /// <summary>Leave empty to generate a strong password.</summary>
    [StringLength(128)] string? Password);

public sealed record UpdateClientRequest(
    [Required, StringLength(200, MinimumLength = 2)] string FullName,
    [Required, RegularExpression(UsernameRules.Pattern, ErrorMessage = UsernameRules.Message)] string Username,
    [EmailAddress, StringLength(256)] string? Email,
    [StringLength(32)] string? Phone,
    [StringLength(4000)] string? Notes);

public sealed record ResetPasswordRequest([StringLength(128)] string? Password);

/// <summary>Returned once, right after creation or reset. Passwords are never retrievable afterwards.</summary>
public sealed record CredentialsResponse(int ClientId, string Username, string? Password);

public static class UsernameRules
{
    public const string Pattern = "^[A-Za-z0-9._-]{3,64}$";
    public const string Message = "Use 3–64 letters, numbers, dots, dashes or underscores.";
}

[ApiController]
[Route("api/admin/clients")]
[Authorize(Policy = Policies.Admin)]
public sealed class AdminClientsController(
    AppDbContext db,
    PasswordService passwords,
    SessionService sessions,
    AuditService audit,
    TimeProvider clock) : ControllerBase
{
    [HttpGet]
    public async Task<Paged<ClientListItem>> List(string? search, string? status, int page = 1, int pageSize = 25, CancellationToken ct = default)
    {
        var q = db.Clients.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(c => c.FullName.Contains(s) || (c.Email != null && c.Email.Contains(s)) || (c.Phone != null && c.Phone.Contains(s))
                             || c.Users.Any(u => u.Username.Contains(s)));
        }
        q = status switch
        {
            "active" => q.Where(c => c.IsActive),
            "disabled" => q.Where(c => !c.IsActive),
            _ => q,
        };

        return await q.OrderByDescending(c => c.CreatedAt)
            .Select(c => new ClientListItem(c.Id, c.FullName,
                c.Users.OrderBy(u => u.Id).Select(u => u.Username).FirstOrDefault() ?? "",
                c.Email, c.Phone, c.IsActive, c.Albums.Count(),
                c.Users.Max(u => u.LastLoginAt), c.CreatedAt))
            .ToPagedAsync(page, pageSize, ct);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ClientDetail>> Get(int id, CancellationToken ct)
    {
        var client = await db.Clients.AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => new
            {
                c.Id, c.FullName, c.Email, c.Phone, c.Notes, c.IsActive, c.CreatedAt,
                User = c.Users.OrderBy(u => u.Id).Select(u => new { u.Id, u.Username, u.LastLoginAt }).FirstOrDefault(),
                Albums = c.Albums.OrderByDescending(a => a.CreatedAt)
                    .Select(a => new ClientAlbumSummary(a.Id, a.Title, a.Status, a.FileCount, a.TotalBytes, a.EventDate)).ToList(),
            })
            .SingleOrDefaultAsync(ct);
        if (client is null) return this.NotFoundProblem("Client not found.");

        var userId = client.User?.Id;
        var activity = userId is null ? [] : await db.AuditLogs.AsNoTracking()
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.CreatedAt).Take(20)
            .Select(a => new ActivityItem(a.Action, a.CreatedAt, a.IpAddress, a.Details))
            .ToListAsync(ct);

        return new ClientDetail(client.Id, client.FullName, client.User?.Username ?? "", client.Email, client.Phone, client.Notes,
            client.IsActive, client.User?.LastLoginAt, client.CreatedAt, client.Albums, activity);
    }

    [HttpPost]
    public async Task<ActionResult<CredentialsResponse>> Create(CreateClientRequest request, CancellationToken ct)
    {
        var username = AuthController.NormalizeUsername(request.Username);
        var generated = string.IsNullOrEmpty(request.Password);
        var password = generated ? PasswordService.Generate() : request.Password!;
        if (!generated && passwords.Validate(password) is { } pwError) return this.FieldError("password", pwError);
        if (await db.Users.AnyAsync(u => u.Username == username, ct)) return this.ConflictProblem("That username is already taken.");

        var client = new Client
        {
            FullName = request.FullName.Trim(),
            Email = Clean(request.Email),
            Phone = Clean(request.Phone),
            Notes = Clean(request.Notes),
        };
        var user = new User { Username = username, Role = UserRole.Client, Client = client };
        user.PasswordHash = passwords.Hash(user, password);
        db.Users.Add(user);

        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation()) { return this.ConflictProblem("That username is already taken."); }

        await audit.WriteAsync(AuditActions.ClientCreated, "Client", client.Id, new { username }, ct: ct);
        return CreatedAtAction(nameof(Get), new { id = client.Id }, new CredentialsResponse(client.Id, username, password));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, UpdateClientRequest request, CancellationToken ct)
    {
        var client = await db.Clients.Include(c => c.Users).SingleOrDefaultAsync(c => c.Id == id, ct);
        if (client is null) return this.NotFoundProblem("Client not found.");

        var username = AuthController.NormalizeUsername(request.Username);
        var user = client.Users.OrderBy(u => u.Id).FirstOrDefault();
        if (user is not null && user.Username != username)
        {
            if (await db.Users.AnyAsync(u => u.Username == username && u.Id != user.Id, ct))
                return this.ConflictProblem("That username is already taken.");
            user.Username = username;
        }

        client.FullName = request.FullName.Trim();
        client.Email = Clean(request.Email);
        client.Phone = Clean(request.Phone);
        client.Notes = Clean(request.Notes);
        audit.Add(AuditActions.ClientUpdated, "Client", id);

        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation()) { return this.ConflictProblem("That username is already taken."); }
        return NoContent();
    }

    [HttpPost("{id:int}/disable")]
    public Task<IActionResult> Disable(int id, CancellationToken ct) => SetActive(id, false, ct);

    [HttpPost("{id:int}/enable")]
    public Task<IActionResult> Enable(int id, CancellationToken ct) => SetActive(id, true, ct);

    private async Task<IActionResult> SetActive(int id, bool active, CancellationToken ct)
    {
        var client = await db.Clients.SingleOrDefaultAsync(c => c.Id == id, ct);
        if (client is null) return this.NotFoundProblem("Client not found.");
        client.IsActive = active;
        audit.Add(active ? AuditActions.ClientEnabled : AuditActions.ClientDisabled, "Client", id);
        await db.SaveChangesAsync(ct);
        if (!active) await sessions.RevokeAllForClientAsync(id, ct);
        return NoContent();
    }

    [HttpPost("{id:int}/reset-password")]
    public async Task<ActionResult<CredentialsResponse>> ResetPassword(int id, ResetPasswordRequest request, CancellationToken ct)
    {
        var user = await db.Users.Where(u => u.ClientId == id).OrderBy(u => u.Id).FirstOrDefaultAsync(ct);
        if (user is null) return this.NotFoundProblem("Client not found.");

        var generated = string.IsNullOrEmpty(request.Password);
        var password = generated ? PasswordService.Generate() : request.Password!;
        if (!generated && passwords.Validate(password) is { } pwError) return this.FieldError("password", pwError);

        user.PasswordHash = passwords.Hash(user, password);
        user.SecurityStamp = Guid.NewGuid();
        user.FailedLoginCount = 0;
        user.LockoutEndUtc = null;
        audit.Add(AuditActions.PasswordReset, "Client", id);
        await db.SaveChangesAsync(ct);
        await sessions.RevokeAllForUserAsync(user.Id, ct);

        return new CredentialsResponse(id, user.Username, password);
    }

    /// <summary>Soft delete: hides the client, its logins and albums. Drive files are never touched.</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var client = await db.Clients.Include(c => c.Users).Include(c => c.Albums).SingleOrDefaultAsync(c => c.Id == id, ct);
        if (client is null) return this.NotFoundProblem("Client not found.");

        var now = clock.GetUtcNow().UtcDateTime;
        await sessions.RevokeAllForClientAsync(id, ct);
        client.DeletedAt = now;
        client.IsActive = false;
        foreach (var u in client.Users)
        {
            u.DeletedAt = now;
            u.IsActive = false;
            u.SecurityStamp = Guid.NewGuid();
        }
        foreach (var a in client.Albums)
        {
            a.DeletedAt = now;
            a.Status = AlbumStatus.Disabled;
        }
        audit.Add(AuditActions.ClientDeleted, "Client", id, new { client.FullName });
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
