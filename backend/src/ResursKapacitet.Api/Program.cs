using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ResursKapacitet.Api.Data;
using ResursKapacitet.Api.Endpoints;
using ResursKapacitet.Api.Services;

// Används av containerns HEALTHCHECK (den officiella .NET-imagen saknar curl/wget).
if (args.Contains("--healthcheck"))
{
    var port = Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS")?.Split(';')[0] ?? "8080";
    try
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        var res = await http.GetAsync($"http://127.0.0.1:{port}/api/health");
        return res.IsSuccessStatusCode ? 0 : 1;
    }
    catch
    {
        return 1;
    }
}

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

// ---- Datakatalog (SQLite-databas, nycklar, backuper) ----
var dataDir = config["DATA_DIR"];
if (string.IsNullOrWhiteSpace(dataDir)) dataDir = Path.Combine(builder.Environment.ContentRootPath, "data");
Directory.CreateDirectory(dataDir);
var connectionString = config.GetConnectionString("Default");
if (string.IsNullOrWhiteSpace(connectionString))
    connectionString = $"Data Source={Path.Combine(dataDir, "resurs.db")}";

builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlite(connectionString));
builder.Services.AddSingleton<SettingsStore>();
builder.Services.AddSingleton<AppInfo>();
builder.Services.AddScoped<PlanningService>();
builder.Services.AddProblemDetails();

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataDir, "keys")))
    .SetApplicationName("resurs-kapacitet");

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.Cookie.Name = "rk_auth";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Strict;
        o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        o.ExpireTimeSpan = TimeSpan.FromHours(12);
        o.SlidingExpiration = true;
        o.Events.OnRedirectToLogin = ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        o.Events.OnRedirectToAccessDenied = ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("Planner", p => p.RequireRole(nameof(UserRole.Planerare), nameof(UserRole.Admin)))
    .AddPolicy("Admin", p => p.RequireRole(nameof(UserRole.Admin)));

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("login", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "okänd",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
});

builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

var app = builder.Build();

// ---- Databas: backup före migrering, migrera, seed ----
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
    await DatabaseMaintenance.MigrateWithBackupAsync(db, dataDir, logger);
    await Seed.RunAsync(db, config, logger);
}

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();

app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["X-Frame-Options"] = "SAMEORIGIN";
    ctx.Response.Headers["Referrer-Policy"] = "same-origin";
    await next();
});

app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        // Filer med hash i namnet (Vite /assets) cachas länge, index.html aldrig – då syns nya versioner direkt.
        ctx.Context.Response.Headers.CacheControl = ctx.Context.Request.Path.StartsWithSegments("/assets")
            ? "public, max-age=31536000, immutable"
            : "no-cache";
    },
});

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// Om admin har valt "Kräv inloggning för att se planeringen" spärras läs-API:erna för anonyma.
app.Use(async (ctx, next) =>
{
    var path = ctx.Request.Path;
    if (path.StartsWithSegments("/api") &&
        !path.StartsWithSegments("/api/auth") &&
        !path.StartsWithSegments("/api/health") &&
        !path.StartsWithSegments("/api/version") &&
        !path.StartsWithSegments("/api/releases") &&
        ctx.User.Identity?.IsAuthenticated != true)
    {
        var settings = await ctx.RequestServices.GetRequiredService<SettingsStore>().GetAsync(ctx.RequestAborted);
        if (settings.RequireLoginToView)
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }
    }
    await next();
});

app.MapGet("/api/health", async (AppDbContext db) =>
    await db.Database.CanConnectAsync() ? Results.Ok(new { status = "ok" }) : Results.StatusCode(503));
app.MapGet("/api/version", (AppInfo info) => info.Version);
app.MapGet("/api/releases", (AppInfo info) => info.ReleaseNotes);

app.MapGet("/api/admin/backup", async (AppDbContext db) =>
{
    var file = Path.Combine(Path.GetTempPath(), $"resurs-backup-{DateTime.UtcNow:yyyyMMddHHmmss}.db");
    await db.Database.ExecuteSqlRawAsync("VACUUM INTO {0}", file);
    var bytes = await File.ReadAllBytesAsync(file);
    File.Delete(file);
    return Results.File(bytes, "application/vnd.sqlite3", $"resurs-kapacitet-{DateTime.Now:yyyy-MM-dd-HHmm}.db");
}).RequireAuthorization("Admin");

app.MapAuthEndpoints();
app.MapMasterDataEndpoints();
app.MapAssignmentEndpoints();
app.MapAllocationEndpoints();
app.MapAdminEndpoints();
app.MapImportExportEndpoints();

app.Map("/api/{**rest}", () => Results.NotFound());
app.MapFallbackToFile("index.html", new StaticFileOptions
{
    OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = "no-cache",
});

app.Run();
return 0;

public partial class Program;

internal static class DatabaseMaintenance
{
    public static async Task MigrateWithBackupAsync(AppDbContext db, string dataDir, ILogger logger)
    {
        var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
        if (pending.Count == 0) return;

        var applied = (await db.Database.GetAppliedMigrationsAsync()).ToList();
        if (applied.Count > 0 && db.Database.GetDbConnection() is SqliteConnection conn &&
            File.Exists(conn.DataSource))
        {
            var backupDir = Path.Combine(dataDir, "backups");
            Directory.CreateDirectory(backupDir);
            var target = Path.Combine(backupDir, $"resurs-{DateTime.UtcNow:yyyyMMdd-HHmmss}-fore-{pending[^1]}.db");
            // VACUUM INTO ger en konsistent kopia även när databasen kör i WAL-läge.
            await db.Database.ExecuteSqlRawAsync("VACUUM INTO {0}", target);
            logger.LogInformation("Säkerhetskopia före uppgradering: {Backup}", target);

            // Behåll de 10 senaste backuperna.
            foreach (var old in new DirectoryInfo(backupDir).GetFiles("resurs-*.db")
                         .OrderByDescending(f => f.CreationTimeUtc).Skip(10))
                old.Delete();
        }

        logger.LogInformation("Kör databasmigreringar: {Migrations}", string.Join(", ", pending));
        await db.Database.MigrateAsync();
    }
}
