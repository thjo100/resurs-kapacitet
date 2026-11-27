using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using ResursKapacitet.Api.Data;
using ResursKapacitet.Api.Services;

namespace ResursKapacitet.Api.Endpoints;

public record TeamRequest(string Name, string? Lead, string? Description, string? Color, int? SortOrder, bool? IsActive);

public record ResourceRequest(
    string Name, int? TeamId, string? Role, ResourceType? Type, int? CapacityPerMonth, int? EmploymentPercent,
    DateOnly? StartDate, DateOnly? EndDate, bool? IsActive, string? Notes, int? SortOrder);

public record ObjectRequest(string Name, ObjectType? Type, string? Owner, int? BudgetHoursPerYear,
    string? Description, string? Color, bool? IsActive);

public record AbsenceRequest(int Year, int Month, int? Hours, string? Reason);

public static class MasterDataEndpoints
{
    public static void MapMasterDataEndpoints(this IEndpointRouteBuilder app)
    {
        MapTeams(app);
        MapResources(app);
        MapObjects(app);
    }

    private static IResult? Validate(string? name, string what) =>
        string.IsNullOrWhiteSpace(name) ? Results.Problem($"{what} måste ha ett namn.", statusCode: 400) : null;

    // ---------------- Team ----------------
    private static void MapTeams(IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/teams");

        g.MapGet("/", async (AppDbContext db) =>
            (await db.Teams.AsNoTracking().OrderBy(t => t.SortOrder).ThenBy(t => t.Name).ToListAsync())
            .Select(TeamDto.From));

        g.MapPost("/", async (TeamRequest req, AppDbContext db, ClaimsPrincipal user) =>
        {
            if (Validate(req.Name, "Teamet") is { } err) return err;
            var name = req.Name.Trim();
            if (await db.Teams.AnyAsync(t => t.Name == name))
                return Results.Problem($"Det finns redan ett team som heter {name}.", statusCode: 409);
            var team = new Team
            {
                Name = name, Lead = req.Lead, Description = req.Description, Color = req.Color ?? "#4f6bed",
                SortOrder = req.SortOrder ?? (await db.Teams.MaxAsync(t => (int?)t.SortOrder) ?? 0) + 1,
                IsActive = req.IsActive ?? true,
            };
            db.Teams.Add(team);
            await db.SaveChangesAsync();
            db.Log(user, "Team", team.Id, "Skapad", $"Team {team.Name} skapades");
            await db.SaveChangesAsync();
            return Results.Ok(TeamDto.From(team));
        }).RequireAuthorization("Admin");

        g.MapPut("/{id:int}", async (int id, TeamRequest req, AppDbContext db, ClaimsPrincipal user) =>
        {
            if (Validate(req.Name, "Teamet") is { } err) return err;
            var team = await db.Teams.FindAsync(id);
            if (team is null) return Results.NotFound();
            var name = req.Name.Trim();
            if (await db.Teams.AnyAsync(t => t.Name == name && t.Id != id))
                return Results.Problem($"Det finns redan ett team som heter {name}.", statusCode: 409);
            var diff = ChangeLog.Diff(("Namn", team.Name, name), ("Teamledare", team.Lead, req.Lead),
                ("Aktiv", team.IsActive, req.IsActive ?? team.IsActive));
            team.Name = name;
            team.Lead = req.Lead;
            team.Description = req.Description;
            team.Color = req.Color ?? team.Color;
            team.SortOrder = req.SortOrder ?? team.SortOrder;
            team.IsActive = req.IsActive ?? team.IsActive;
            db.Log(user, "Team", team.Id, "Ändrad", $"Team {team.Name} ändrades" + (diff.Length > 0 ? $": {diff}" : ""));
            await db.SaveChangesAsync();
            return Results.Ok(TeamDto.From(team));
        }).RequireAuthorization("Admin");

        g.MapDelete("/{id:int}", async (int id, AppDbContext db, ClaimsPrincipal user) =>
        {
            var team = await db.Teams.FindAsync(id);
            if (team is null) return Results.NotFound();
            db.Teams.Remove(team);
            db.Log(user, "Team", id, "Borttagen", $"Team {team.Name} togs bort (resurserna ligger kvar utan team)");
            await db.SaveChangesAsync();
            return Results.NoContent();
        }).RequireAuthorization("Admin");

        g.MapPut("/order", async (int[] ids, AppDbContext db, ClaimsPrincipal user) =>
        {
            var teams = await db.Teams.ToListAsync();
            for (var i = 0; i < ids.Length; i++)
                if (teams.FirstOrDefault(t => t.Id == ids[i]) is { } t) t.SortOrder = i + 1;
            db.Log(user, "Team", null, "Sorterad", "Teamens ordning ändrades");
            await db.SaveChangesAsync();
            return Results.NoContent();
        }).RequireAuthorization("Admin");
    }

    // ---------------- Resurser ----------------
    private static void MapResources(IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/resources");

        g.MapGet("/", async (AppDbContext db, SettingsStore store, int? year) =>
        {
            var y = year ?? DateTime.Now.Year;
            var settings = await store.GetAsync();
            var overrides = await db.CapacityOverrides.AsNoTracking().Where(o => o.Year == y).ToListAsync();
            var list = await db.Resources.AsNoTracking().OrderBy(r => r.SortOrder).ThenBy(r => r.Name).ToListAsync();
            return list.Select(r => ToDto(r, settings, y, overrides));
        });

        g.MapPost("/", async (ResourceRequest req, AppDbContext db, ClaimsPrincipal user, SettingsStore store) =>
        {
            if (ValidateResource(req) is { } err) return err;
            var r = new Resource();
            Apply(r, req);
            r.SortOrder = req.SortOrder ?? (await db.Resources.MaxAsync(x => (int?)x.SortOrder) ?? 0) + 1;
            db.Resources.Add(r);
            await db.SaveChangesAsync();
            db.Log(user, "Resurs", r.Id, "Skapad", $"Resurs {r.Name} skapades", resourceId: r.Id);
            await db.SaveChangesAsync();
            return Results.Ok(ToDto(r, await store.GetAsync(), DateTime.Now.Year, []));
        }).RequireAuthorization("Admin");

        g.MapPut("/{id:int}", async (int id, ResourceRequest req, AppDbContext db, ClaimsPrincipal user, SettingsStore store) =>
        {
            if (ValidateResource(req) is { } err) return err;
            var r = await db.Resources.Include(x => x.Team).FirstOrDefaultAsync(x => x.Id == id);
            if (r is null) return Results.NotFound();
            var teamNames = await db.Teams.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.Name);
            var diff = ChangeLog.Diff(
                ("Namn", r.Name, req.Name.Trim()),
                ("Team", r.TeamId is { } a ? teamNames.GetValueOrDefault(a) : null,
                    req.TeamId is { } b ? teamNames.GetValueOrDefault(b) : null),
                ("Roll", r.Role, req.Role),
                ("Typ", r.Type, req.Type ?? r.Type),
                ("Kapacitet", r.CapacityPerMonth, req.CapacityPerMonth),
                ("Sysselsättning %", r.EmploymentPercent, req.EmploymentPercent ?? r.EmploymentPercent),
                ("Start", r.StartDate, req.StartDate),
                ("Slut", r.EndDate, req.EndDate),
                ("Aktiv", r.IsActive, req.IsActive ?? r.IsActive));
            Apply(r, req);
            db.Log(user, "Resurs", r.Id, "Ändrad", $"Resurs {r.Name} ändrades" + (diff.Length > 0 ? $": {diff}" : ""),
                resourceId: r.Id);
            await db.SaveChangesAsync();
            var overrides = await db.CapacityOverrides.AsNoTracking().Where(o => o.Year == DateTime.Now.Year).ToListAsync();
            return Results.Ok(ToDto(r, await store.GetAsync(), DateTime.Now.Year, overrides));
        }).RequireAuthorization("Admin");

        g.MapDelete("/{id:int}", async (int id, AppDbContext db, ClaimsPrincipal user) =>
        {
            var r = await db.Resources.FindAsync(id);
            if (r is null) return Results.NotFound();
            var allocations = await db.Allocations.CountAsync(a => a.ResourceId == id);
            db.Resources.Remove(r);
            db.Log(user, "Resurs", id, "Borttagen",
                $"Resurs {r.Name} togs bort" + (allocations > 0 ? $" ({allocations} uppdragsrader togs också bort)" : ""),
                resourceId: id);
            await db.SaveChangesAsync();
            return Results.NoContent();
        }).RequireAuthorization("Admin");

        // Frånvaro / avvikande kapacitet per månad
        g.MapPut("/{id:int}/absences", async (int id, AbsenceRequest req, AppDbContext db, ClaimsPrincipal user) =>
        {
            if (req.Month is < 1 or > 12) return Results.Problem("Ogiltig månad.", statusCode: 400);
            if (req.Hours is < 0 or > 744) return Results.Problem("Ogiltigt antal timmar.", statusCode: 400);
            var r = await db.Resources.FindAsync(id);
            if (r is null) return Results.NotFound();
            var existing = await db.CapacityOverrides.FirstOrDefaultAsync(o =>
                o.ResourceId == id && o.Year == req.Year && o.Month == req.Month);
            var monthName = $"{ChangeLog.MonthNames[req.Month - 1]} {req.Year}";
            if (req.Hours is null)
            {
                if (existing is null) return Results.NoContent();
                db.CapacityOverrides.Remove(existing);
                db.Log(user, "Kapacitet", id, "Borttagen", $"{r.Name}: avvikande kapacitet {monthName} togs bort",
                    resourceId: id);
            }
            else
            {
                existing ??= db.CapacityOverrides.Add(new CapacityOverride
                    { ResourceId = id, Year = req.Year, Month = req.Month }).Entity;
                existing.Hours = req.Hours.Value;
                existing.Reason = string.IsNullOrWhiteSpace(req.Reason) ? null : req.Reason.Trim();
                db.Log(user, "Kapacitet", id, "Ändrad",
                    $"{r.Name}: kapacitet {monthName} satt till {req.Hours} h" +
                    (existing.Reason is null ? "" : $" ({existing.Reason})"), resourceId: id);
            }

            await db.SaveChangesAsync();
            return Results.NoContent();
        }).RequireAuthorization("Planner");

        g.MapPut("/order", async (int[] ids, AppDbContext db, ClaimsPrincipal user) =>
        {
            var resources = await db.Resources.ToListAsync();
            for (var i = 0; i < ids.Length; i++)
                if (resources.FirstOrDefault(r => r.Id == ids[i]) is { } r) r.SortOrder = i + 1;
            db.Log(user, "Resurs", null, "Sorterad", "Resursernas ordning ändrades");
            await db.SaveChangesAsync();
            return Results.NoContent();
        }).RequireAuthorization("Planner");
    }

    private static IResult? ValidateResource(ResourceRequest req)
    {
        if (Validate(req.Name, "Resursen") is { } err) return err;
        if (req.EmploymentPercent is < 0 or > 100)
            return Results.Problem("Sysselsättningsgraden måste vara 0–100 %.", statusCode: 400);
        if (req.CapacityPerMonth is < 0 or > 400)
            return Results.Problem("Kapaciteten måste vara 0–400 h/mån.", statusCode: 400);
        if (req.StartDate is { } s && req.EndDate is { } e && e < s)
            return Results.Problem("Slutdatum kan inte vara före startdatum.", statusCode: 400);
        return null;
    }

    private static void Apply(Resource r, ResourceRequest req)
    {
        r.Name = req.Name.Trim();
        r.TeamId = req.TeamId;
        r.Role = string.IsNullOrWhiteSpace(req.Role) ? null : req.Role.Trim();
        r.Type = req.Type ?? r.Type;
        r.CapacityPerMonth = req.CapacityPerMonth;
        r.EmploymentPercent = req.EmploymentPercent ?? r.EmploymentPercent;
        r.StartDate = req.StartDate;
        r.EndDate = req.EndDate;
        r.IsActive = req.IsActive ?? r.IsActive;
        r.Notes = req.Notes;
        r.SortOrder = req.SortOrder ?? r.SortOrder;
    }

    private static ResourceDto ToDto(Resource r, AppSettings settings, int year, List<CapacityOverride> overrides) =>
        ResourceDto.From(r, CapacityCalculator.ForYear(r, settings, year, overrides),
            overrides.Where(o => o.ResourceId == r.Id).Select(o => new AbsenceDto(o.Month, o.Hours, o.Reason)).ToList());

    // ---------------- Förvaltningsobjekt / projekt ----------------
    private static void MapObjects(IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/objects");

        g.MapGet("/", async (AppDbContext db) =>
            (await db.Objects.AsNoTracking().OrderBy(o => o.Name).ToListAsync()).Select(ObjectDto.From));

        g.MapPost("/", async (ObjectRequest req, AppDbContext db, ClaimsPrincipal user) =>
        {
            if (Validate(req.Name, "Objektet") is { } err) return err;
            var name = req.Name.Trim();
            if (await db.Objects.AnyAsync(o => o.Name == name))
                return Results.Problem($"Det finns redan ett objekt som heter {name}.", statusCode: 409);
            var o = new WorkObject
            {
                Name = name, Type = req.Type ?? ObjectType.Forvaltning, Owner = req.Owner,
                BudgetHoursPerYear = req.BudgetHoursPerYear, Description = req.Description,
                Color = req.Color ?? "#0f9d8a", IsActive = req.IsActive ?? true,
            };
            db.Objects.Add(o);
            await db.SaveChangesAsync();
            db.Log(user, "Objekt", o.Id, "Skapad", $"{TypeName(o.Type)} {o.Name} skapades");
            await db.SaveChangesAsync();
            return Results.Ok(ObjectDto.From(o));
        }).RequireAuthorization("Admin");

        g.MapPut("/{id:int}", async (int id, ObjectRequest req, AppDbContext db, ClaimsPrincipal user) =>
        {
            if (Validate(req.Name, "Objektet") is { } err) return err;
            var o = await db.Objects.FindAsync(id);
            if (o is null) return Results.NotFound();
            var name = req.Name.Trim();
            if (await db.Objects.AnyAsync(x => x.Name == name && x.Id != id))
                return Results.Problem($"Det finns redan ett objekt som heter {name}.", statusCode: 409);
            var diff = ChangeLog.Diff(("Namn", o.Name, name), ("Typ", o.Type, req.Type ?? o.Type),
                ("Ägare", o.Owner, req.Owner), ("Budget h/år", o.BudgetHoursPerYear, req.BudgetHoursPerYear),
                ("Aktiv", o.IsActive, req.IsActive ?? o.IsActive));
            o.Name = name;
            o.Type = req.Type ?? o.Type;
            o.Owner = req.Owner;
            o.BudgetHoursPerYear = req.BudgetHoursPerYear;
            o.Description = req.Description;
            o.Color = req.Color ?? o.Color;
            o.IsActive = req.IsActive ?? o.IsActive;
            db.Log(user, "Objekt", o.Id, "Ändrad", $"{TypeName(o.Type)} {o.Name} ändrades" + (diff.Length > 0 ? $": {diff}" : ""));
            await db.SaveChangesAsync();
            return Results.Ok(ObjectDto.From(o));
        }).RequireAuthorization("Admin");

        g.MapDelete("/{id:int}", async (int id, AppDbContext db, ClaimsPrincipal user) =>
        {
            var o = await db.Objects.FindAsync(id);
            if (o is null) return Results.NotFound();
            db.Objects.Remove(o);
            db.Log(user, "Objekt", id, "Borttagen", $"{TypeName(o.Type)} {o.Name} togs bort (uppdragen ligger kvar utan objekt)");
            await db.SaveChangesAsync();
            return Results.NoContent();
        }).RequireAuthorization("Admin");
    }

    public static string TypeName(ObjectType t) => t == ObjectType.Projekt ? "Projekt" : "Förvaltningsobjekt";
}
