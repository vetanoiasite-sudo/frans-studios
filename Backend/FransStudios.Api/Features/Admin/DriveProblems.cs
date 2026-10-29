using FransStudios.Api.GoogleDrive;
using Microsoft.AspNetCore.Mvc;

namespace FransStudios.Api.Features.Admin;

/// <summary>Turns upload / Drive errors into ProblemDetails the admin can read (album uploads and the Drive page).</summary>
internal static class DriveProblems
{
    public static async Task<IActionResult> RunDriveAsync(this ControllerBase c, Func<Task<IActionResult>> action,
        int notFoundStatus = StatusCodes.Status404NotFound, string notFoundTitle = "That file or folder was not found in Drive.")
    {
        try { return await action(); }
        catch (DriveUploadException ex) { return c.Problem(statusCode: ex.StatusCode, title: ex.Message); }
        catch (DriveNotConnectedException ex) { return c.Problem(statusCode: 503, title: ex.Message); }
        catch (DriveApiException ex) when (ex.Reason is "storageQuotaExceeded" or "quotaExceeded")
        {
            return c.Problem(statusCode: 507, title: "The studio's Google Drive is full. Free up space or upgrade the Google storage plan.");
        }
        catch (DriveApiException ex) when (ex.StatusCode == 404) { return c.Problem(statusCode: notFoundStatus, title: notFoundTitle); }
        catch (DriveApiException ex) when (ex.StatusCode == 403)
        {
            return c.Problem(statusCode: 403, title: $"Google Drive doesn't allow that: {ex.Message}");
        }
        catch (DriveApiException ex) { return c.Problem(statusCode: 502, title: $"Google Drive refused the request: {ex.Message}"); }
    }
}
