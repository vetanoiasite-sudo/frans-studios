using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace FransStudios.Api.Infrastructure;

public sealed record Paged<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

public static class ApiHelpers
{
    public static (int Page, int PageSize) Clamp(int page, int pageSize, int max = 100) =>
        (Math.Max(1, page), Math.Clamp(pageSize, 1, max));

    public static async Task<Paged<T>> ToPagedAsync<T>(this IQueryable<T> query, int page, int pageSize, CancellationToken ct)
    {
        (page, pageSize) = Clamp(page, pageSize);
        var total = await query.CountAsync(ct);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new Paged<T>(items, total, page, pageSize);
    }

    /// <summary>True for SQL Server unique-index / primary-key violations (2601, 2627).</summary>
    public static bool IsUniqueViolation(this DbUpdateException ex) =>
        ex.InnerException is SqlException { Number: 2601 or 2627 };

    public static ActionResult FieldError(this ControllerBase c, string field, string message) =>
        c.ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { [field] = [message] }));

    public static ActionResult ConflictProblem(this ControllerBase c, string title) =>
        c.Problem(statusCode: StatusCodes.Status409Conflict, title: title);

    public static ActionResult NotFoundProblem(this ControllerBase c, string title = "Not found.") =>
        c.Problem(statusCode: StatusCodes.Status404NotFound, title: title);
}
