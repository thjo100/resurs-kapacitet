using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using ResursKapacitet.Api.Data;
using ResursKapacitet.Api.Services;

namespace ResursKapacitet.Api.Endpoints;

public record UserDto(int Id, string UserName, string DisplayName, UserRole Role, bool IsActive,
    bool MustChangePassword, DateTime CreatedAt, DateTime? LastLoginAt);

public record UserRequest(string UserName, string DisplayName, UserRole? Role, bool? IsActive, string? Password,
    bool? MustChangePassword);

public record SettingsRequest(
    string? OrganizationName, int? StandardCapacity, int? PlanningCeilingPercent, double[]? MonthFactors,
    int? ThresholdLow, int? ThresholdHigh, int? ThresholdCritical, bool? RequireLoginToView,
    int? ChangeLogRetentionMonths);

public record ChangeDto(long Id, DateTime Timestamp, string User, string EntityType, int? EntityId, string Action,
    string Summary, int? ResourceId, int? AssignmentId);

public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        MapUsers(app);
        MapSettings(app);
        MapChanges(app);
    }

    private static UserDto ToDto(User u) => new(u.Id, u.UserName, u.DisplayName, u.Role, u.IsActive,
        u.MustChangePassword, u.CreatedAt, u.LastLoginAt);

    private static readonly Dictionary<UserRole, string> RoleNames = new()
    {
        [UserRole.Lasare] = "Läsare", [UserRole.Planerare] = "Planerare", [UserRole.Admin] = "Admin",
    };

    private static void MapUsers(IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/users").RequireAuthorization("Admin");

        g.MapGet("/", async (AppDbContext db) =>
            (await db.Users.AsNoTracking().OrderBy(u => u.DisplayName).ToListAsync()).Select(ToDto));

        g.MapPost("/", async (UserRequest req, AppDbContext db, ClaimsPrincipal user) =>
        {
            var userName = (req.UserName ?? "").Trim().ToLowerInvariant();
            if (userName.Length < 2) return Results.Problem("Användarnamnet måste vara minst 2 tecken.", statusCode: 400);
            if ((req.Password ?? "").Length < 6)
                return Results.Problem("Lösenordet måste vara minst 6 tecken.", statusCode: 400);
            if (await db.Users.AnyAsync(u => u.UserName == userName))
                return Results.Problem($"Användarnamnet {userName} är upptaget.", statusCode: 409);
            var u = new User
            {
                UserName = userName,
                DisplayName = string.IsNullOrWhiteSpace(req.DisplayName) ? userName : req.DisplayName.Trim(),
                Role = req.Role ?? UserRole.Lasare,
                IsActive = req.IsActive ?? true,
                MustChangePassword = req.MustChangePassword ?? true,
            };
            u.PasswordHash = Passwords.Hash(u, req.Password!);
            db.Users.Add(u);
            await db.SaveChangesAsync();
            db.Log(user, "Användare", u.Id, "Skapad", $"Användare {u.DisplayName} ({RoleNames[u.Role]}) skapades");
            await db.SaveChangesAsync();
            return Results.Ok(ToDto(u));
        });

        g.MapPut("/{id:int}", async (int id, UserRequest req, AppDbContext db, ClaimsPrincipal user) =>
        {
            var u = await db.Users.FindAsync(id);
            if (u is null) return Results.NotFound();
            var newRole = req.Role ?? u.Role;
            var newActive = req.IsActive ?? u.IsActive;
            if (u.Role == UserRole.Admin && (newRole != UserRole.Admin || !newActive) &&
                await db.Users.CountAsync(x => x.Role == UserRole.Admin && x.IsActive) <= 1)
                return Results.Problem("Det måste finnas minst en aktiv admin.", statusCode: 400);

            var diff = ChangeLog.Diff(("Namn", u.DisplayName, req.DisplayName), ("Roll", RoleNames[u.Role], RoleNames[newRole]),
                ("Aktiv", u.IsActive, newActive));
            if (!string.IsNullOrWhiteSpace(req.DisplayName)) u.DisplayName = req.DisplayName.Trim();
            u.Role = newRole;
            u.IsActive = newActive;
            if (req.MustChangePassword is { } mcp) u.MustChangePassword = mcp;
            if (!string.IsNullOrEmpty(req.Password))
            {
                if (req.Password.Length < 6) return Results.Problem("Lösenordet måste vara minst 6 tecken.", statusCode: 400);
                u.PasswordHash = Passwords.Hash(u, req.Password);
                diff += (diff.Length > 0 ? ", " : "") + "lösenord återställt";
            }
            db.Log(user, "Användare", u.Id, "Ändrad", $"Användare {u.DisplayName} ändrades" + (diff.Length > 0 ? $": {diff}" : ""));
            await db.SaveChangesAsync();
            return Results.Ok(ToDto(u));
        });

        g.MapDelete("/{id:int}", async (int id, AppDbContext db, ClaimsPrincipal user) =>
        {
            var u = await db.Users.FindAsync(id);
            if (u is null) return Results.NotFound();
            if (user.UserId() == id) return Results.Problem("Du kan inte ta bort dig själv.", statusCode: 400);
            if (u.Role == UserRole.Admin && await db.Users.CountAsync(x => x.Role == UserRole.Admin && x.IsActive) <= 1)
                return Results.Problem("Det måste finnas minst en aktiv admin.", statusCode: 400);
            db.Users.Remove(u);
            db.Log(user, "Användare", id, "Borttagen", $"Användare {u.DisplayName} togs bort");
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
    }

    private static void MapSettings(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/settings", async (SettingsStore store) => SettingsDto.From(await store.GetAsync()));

        app.MapPut("/api/settings", async (SettingsRequest req, AppDbContext db, SettingsStore store, ClaimsPrincipal user) =>
        {
            var s = await db.Settings.FirstOrDefaultAsync();
            if (s is null)
            {
                s = new AppSettings();
                db.Settings.Add(s);
            }

            var before = SettingsDto.From(s);
            if (req.OrganizationName is { } on && !string.IsNullOrWhiteSpace(on)) s.OrganizationName = on.Trim();
            if (req.StandardCapacity is { } sc) s.StandardCapacity = Math.Clamp(sc, 0, 400);
            if (req.PlanningCeilingPercent is { } pc) s.PlanningCeilingPercent = Math.Clamp(pc, 10, 100);
            if (req.MonthFactors is { Length: 12 } mf) s.SetMonthFactors(mf);
            if (req.ThresholdLow is { } tl) s.ThresholdLow = Math.Clamp(tl, 0, 300);
            if (req.ThresholdHigh is { } th) s.ThresholdHigh = Math.Clamp(th, 0, 300);
            if (req.ThresholdCritical is { } tc) s.ThresholdCritical = Math.Clamp(tc, 0, 300);
            if (req.RequireLoginToView is { } rl) s.RequireLoginToView = rl;
            if (req.ChangeLogRetentionMonths is { } cr) s.ChangeLogRetentionMonths = Math.Clamp(cr, 1, 120);
            if (!(s.ThresholdLow <= s.ThresholdHigh && s.ThresholdHigh <= s.ThresholdCritical))
                return Results.Problem("Tröskelvärdena måste vara i stigande ordning.", statusCode: 400);

            var after = SettingsDto.From(s);
            var diff = ChangeLog.Diff(
                ("Organisation", before.OrganizationName, after.OrganizationName),
                ("Standardkapacitet", before.StandardCapacity, after.StandardCapacity),
                ("Planeringstak %", before.PlanningCeilingPercent, after.PlanningCeilingPercent),
                ("Månadsfaktorer", string.Join(" ", before.MonthFactors), string.Join(" ", after.MonthFactors)),
                ("Trösklar", $"{before.ThresholdLow}/{before.ThresholdHigh}/{before.ThresholdCritical}",
                    $"{after.ThresholdLow}/{after.ThresholdHigh}/{after.ThresholdCritical}"),
                ("Kräv inloggning för läsning", before.RequireLoginToView, after.RequireLoginToView));
            db.Log(user, "Inställningar", 1, "Ändrad", "Inställningar ändrades" + (diff.Length > 0 ? $": {diff}" : ""));
            await db.SaveChangesAsync();
            store.Invalidate();
            return Results.Ok(after);
        }).RequireAuthorization("Admin");
    }

    private static void MapChanges(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/changes", async (AppDbContext db, int? take, long? before, string? entityType,
            int? resourceId, int? assignmentId, string? q, string? user) =>
        {
            var query = db.ChangeLog.AsNoTracking().AsQueryable();
            if (before is { } b) query = query.Where(c => c.Id < b);
            if (!string.IsNullOrWhiteSpace(entityType)) query = query.Where(c => c.EntityType == entityType);
            if (resourceId is { } rid) query = query.Where(c => c.ResourceId == rid);
            if (assignmentId is { } aid) query = query.Where(c => c.AssignmentId == aid);
            if (!string.IsNullOrWhiteSpace(user)) query = query.Where(c => c.User == user);
            if (!string.IsNullOrWhiteSpace(q))
            {
                var pattern = $"%{q.Trim()}%";
                query = query.Where(c => EF.Functions.Like(c.Summary, pattern) || EF.Functions.Like(c.User, pattern));
            }

            var items = await query.OrderByDescending(c => c.Id).Take(Math.Clamp(take ?? 50, 1, 500))
                .Select(c => new ChangeDto(c.Id, c.Timestamp, c.User, c.EntityType, c.EntityId, c.Action, c.Summary,
                    c.ResourceId, c.AssignmentId))
                .ToListAsync();
            return Results.Ok(items);
        });

        // Lätt anrop för att se om något har ändrats sedan sidan laddades (används för "Ny data finns").
        app.MapGet("/api/changes/latest", async (AppDbContext db) =>
        {
            var latest = await db.ChangeLog.AsNoTracking().OrderByDescending(c => c.Id)
                .Select(c => new { c.Id, c.Timestamp, c.User, c.Summary }).FirstOrDefaultAsync();
            return Results.Ok(latest);
        });
    }
}
