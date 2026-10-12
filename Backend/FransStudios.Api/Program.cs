using System.Text.Json.Serialization;
using FransStudios.Api.Authentication;
using FransStudios.Api.Configuration;
using FransStudios.Api.Data;
using FransStudios.Api.Entities;
using FransStudios.Api.GoogleDrive;
using FransStudios.Api.Infrastructure;
using FransStudios.Api.Seo;
using FransStudios.Api.Storage;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

// ── Options (validated at startup so misconfiguration fails fast) ────────────
builder.Services.AddOptions<SiteOptions>().Bind(config.GetSection(SiteOptions.Section)).ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddOptions<DatabaseOptions>().Bind(config.GetSection(DatabaseOptions.Section));
builder.Services.AddOptions<AuthOptions>().Bind(config.GetSection(AuthOptions.Section)).ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddOptions<StorageOptions>().Bind(config.GetSection(StorageOptions.Section)).ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddOptions<GoogleDriveOptions>().Bind(config.GetSection(GoogleDriveOptions.Section)).ValidateDataAnnotations();
builder.Services.AddOptions<EmailOptions>().Bind(config.GetSection(EmailOptions.Section));

var storage = config.GetSection(StorageOptions.Section).Get<StorageOptions>() ?? new StorageOptions();
var siteSettings = config.GetSection(SiteOptions.Section).Get<SiteOptions>() ?? new SiteOptions();

// ── Data ─────────────────────────────────────────────────────────────────────
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddDbContext<AppDbContext>(o => o
    .UseSqlServer(
        config.GetConnectionString("Default") ?? throw new InvalidOperationException("ConnectionStrings:Default is not set."),
        sql => sql.EnableRetryOnFailure(3))
    // GoogleDriveConnection → User is never navigated, so the soft-delete filter on User can't affect it.
    .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning)));

// ── Data Protection: encrypts auth cookies and the stored Google refresh token ─
var dp = builder.Services.AddDataProtection().SetApplicationName("FransStudios");
if (!string.IsNullOrWhiteSpace(storage.DataProtectionKeysPath))
{
    dp.PersistKeysToFileSystem(new DirectoryInfo(storage.DataProtectionKeysPath));
    if (OperatingSystem.IsWindows()) dp.ProtectKeysWithDpapi(protectToLocalMachine: true);
}

// ── Authentication & authorization ───────────────────────────────────────────
builder.Services.AddHttpContextAccessor();
builder.Services.AddMemoryCache();
builder.Services.AddScoped<CurrentUser>();
builder.Services.AddScoped<SessionService>();
builder.Services.AddScoped<SessionCookieEvents>();
builder.Services.AddSingleton<PasswordService>();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.Cookie.Name = "__Host-fs_auth";
        o.Cookie.HttpOnly = true;
        o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        o.Cookie.SameSite = SameSiteMode.Strict;
        o.Cookie.Path = "/";
        o.SlidingExpiration = true;
        o.ExpireTimeSpan = TimeSpan.FromDays(config.GetValue("Auth:ClientSessionDays", 30));
        o.EventsType = typeof(SessionCookieEvents);
    });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(Policies.Admin, p => p.RequireAuthenticatedUser().RequireRole(nameof(UserRole.Admin)))
    .AddPolicy(Policies.Client, p => p.RequireAuthenticatedUser().RequireRole(nameof(UserRole.Client)).RequireClaim(AppClaims.ClientId));

builder.Services.AddAntiforgery(o =>
{
    o.HeaderName = "X-XSRF-TOKEN";
    o.Cookie.Name = "__Host-fs_af";
    o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    o.Cookie.SameSite = SameSiteMode.Strict;
    o.Cookie.Path = "/";
    o.SuppressXFrameOptionsHeader = true; // set globally by SecurityHeadersMiddleware
});

builder.Services.AddAppRateLimiting(config);

// ── CORS: only used if the SPA is ever hosted on another origin. Same-origin needs none. ─
if (siteSettings.AllowedCorsOrigins.Length > 0)
{
    builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
        .WithOrigins(siteSettings.AllowedCorsOrigins)   // explicit list — never AllowAnyOrigin with credentials
        .AllowCredentials()
        .WithMethods("GET", "POST", "PUT", "DELETE")
        .WithHeaders("Content-Type", "X-XSRF-TOKEN")
        .WithExposedHeaders("Content-Disposition", "Content-Range", "Accept-Ranges")));
}

// ── Google Drive ─────────────────────────────────────────────────────────────
builder.Services.AddHttpClient(DriveHttp.ApiClient, c => c.Timeout = TimeSpan.FromSeconds(60));
builder.Services.AddHttpClient(DriveHttp.MediaClient, c => c.Timeout = Timeout.InfiniteTimeSpan) // lifetime bound to the request instead
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        AutomaticDecompression = System.Net.DecompressionMethods.None,
        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
    });
builder.Services.AddScoped<DriveTokenService>();
builder.Services.AddScoped<DriveClient>();
builder.Services.AddScoped<AlbumSyncService>();
builder.Services.AddScoped<DriveUploadService>();
builder.Services.AddScoped<JpegConversionService>();
builder.Services.AddScoped<AlbumMediaService>();
builder.Services.AddSingleton<AlbumSyncQueue>();
builder.Services.AddHostedService<AlbumSyncWorker>();
builder.Services.AddScoped<DriveMoveService>();
builder.Services.AddSingleton<AlbumMoveQueue>();
builder.Services.AddHostedService<AlbumMoveWorker>();

// ── Public media, email, audit ───────────────────────────────────────────────
builder.Services.AddSingleton<PublicMediaStore>();
builder.Services.AddScoped<AuditService>();
builder.Services.AddSingleton<EmailDispatcher>();
if (string.Equals(config["Email:Provider"], "Smtp", StringComparison.OrdinalIgnoreCase))
    builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();
else
    builder.Services.AddSingleton<IEmailSender, LogOnlyEmailSender>();

// ── MVC / errors ─────────────────────────────────────────────────────────────
builder.Services.AddControllers().AddJsonOptions(o =>
    o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase)));
builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = ctx =>
{
    // Never leak exception details; the trace id lets us find the full error in the server log.
    ctx.ProblemDetails.Extensions.Remove("exception");
    ctx.ProblemDetails.Extensions["traceId"] = ctx.HttpContext.TraceIdentifier;
});

// Behind a reverse proxy other than IIS (IIS in-process passes the real client IP already).
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    foreach (var proxy in config.GetSection("ReverseProxy:KnownProxies").Get<string[]>() ?? [])
        o.KnownProxies.Add(System.Net.IPAddress.Parse(proxy));
});

var app = builder.Build();

// ── One-off CLI commands (run from the deployment folder) ────────────────────
if (args.Length > 0 && await CliCommands.TryRunAsync(args, app.Services)) return;

if (app.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value.MigrateOnStartup)
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
}
Directory.CreateDirectory(storage.PublicMediaPath);
Directory.CreateDirectory(storage.PrivateCachePath);

// ── Pipeline ─────────────────────────────────────────────────────────────────
app.UseForwardedHeaders();
app.UseExceptionHandler(errorApp => errorApp.Run(async ctx =>
{
    var ex = ctx.Features.Get<IExceptionHandlerFeature>()?.Error;
    var (status, title) = ex switch
    {
        DriveNotConnectedException d => (503, d.Message),
        InvalidUploadException u => (400, u.Message),
        BadHttpRequestException b => (b.StatusCode, "Bad request."),
        _ => (500, "Something went wrong. Please try again."),
    };
    await Results.Problem(statusCode: status, title: title).ExecuteAsync(ctx);
}));
app.UseStatusCodePages(async ctx =>
{
    // Bare 401/403/404/405 from the framework become ProblemDetails for API routes.
    if (ctx.HttpContext.Request.Path.StartsWithSegments("/api") && !ctx.HttpContext.Response.HasStarted)
        await Results.Problem(statusCode: ctx.HttpContext.Response.StatusCode).ExecuteAsync(ctx.HttpContext);
});
if (!app.Environment.IsDevelopment()) app.UseHsts();
app.UseHttpsRedirection();
app.UseSecurityHeaders();

// Built React app (wwwroot). Hashed asset files are immutable; index.html is served by SeoMiddleware.
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        if (ctx.Context.Request.Path.StartsWithSegments("/assets"))
            ctx.Context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
    },
});
// Public portfolio media (never private client files). Keys are unique per upload, so cache forever.
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(Path.GetFullPath(storage.PublicMediaPath)),
    RequestPath = PublicMediaStore.RequestPath,
    ServeUnknownFileTypes = false,
    OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = "public, max-age=31536000, immutable",
});

app.UseRouting();
if (siteSettings.AllowedCorsOrigins.Length > 0) app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseApiAntiforgery();
app.UseAuthorization();

app.MapControllers();
app.MapGet("/api/health", async (AppDbContext db, CancellationToken ct) =>
    await db.Database.CanConnectAsync(ct) ? Results.Ok(new { status = "ok" }) : Results.Problem(statusCode: 503, title: "Database unavailable."));
app.MapSeoEndpoints();
app.MapFallback("/api/{**rest}", () => Results.Problem(statusCode: 404, title: "Not found."));
app.UseMiddleware<SeoMiddleware>();

app.Run();

public partial class Program;
