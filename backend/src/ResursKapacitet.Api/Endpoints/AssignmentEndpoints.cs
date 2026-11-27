using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using ResursKapacitet.Api.Data;
using ResursKapacitet.Api.Services;

namespace ResursKapacitet.Api.Endpoints;

public record AssignmentRequest(
    string Name, int? ObjectId, int? EstimatedHours, AssignmentStatus? Status, DateOnly? StartDate,
    DateOnly? EndDate, string? Requester, string? Responsible, string? Description, int? Priority);

public static class AssignmentEndpoints
{
    public static readonly Dictionary<AssignmentStatus, string> StatusNames = new()
    {
        [AssignmentStatus.Backlogg] = "Backlogg",
        [AssignmentStatus.Planerad] = "Planerad",
        [AssignmentStatus.Pagaende] = "Pågående",
        [AssignmentStatus.Pausad] = "Pausad",
        [AssignmentStatus.Klar] = "Klar",
    };

    public static void MapAssignmentEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/assignments");

        g.MapGet("/", async (AppDbContext db) =>
        {
            var assignments = await db.Assignments.AsNoTracking().OrderBy(a => a.Priority).ThenBy(a => a.Name).ToListAsync();
            var totals = await Totals(db);
            return assignments.Select(a => PlanningService.ToDto(a, totals.GetValueOrDefault(a.Id), 0));
        });

        g.MapPost("/", async (AssignmentRequest req, AppDbContext db, ClaimsPrincipal user) =>
        {
            if (Validate(req) is { } err) return err;
            var a = new Assignment();
            Apply(a, req);
            a.Status = req.Status ?? AssignmentStatus.Backlogg;
            // Nya backloggposter hamnar sist i prioritetsordningen.
            a.Priority = req.Priority ?? (await db.Assignments.MaxAsync(x => (int?)x.Priority) ?? 0) + 1;
            db.Assignments.Add(a);
            await db.SaveChangesAsync();
            db.Log(user, "Uppdrag", a.Id, "Skapad",
                $"Uppdrag {a.Name} skapades ({a.EstimatedHours} h, {StatusNames[a.Status]})", assignmentId: a.Id);
            await db.SaveChangesAsync();
            return Results.Ok(PlanningService.ToDto(a, 0, 0));
        }).RequireAuthorization("Planner");

        g.MapPut("/{id:int}", async (int id, AssignmentRequest req, AppDbContext db, ClaimsPrincipal user) =>
        {
            if (Validate(req) is { } err) return err;
            var a = await db.Assignments.FindAsync(id);
            if (a is null) return Results.NotFound();
            var objectNames = await db.Objects.AsNoTracking().ToDictionaryAsync(o => o.Id, o => o.Name);
            var newStatus = req.Status ?? a.Status;
            var diff = ChangeLog.Diff(
                ("Namn", a.Name, req.Name.Trim()),
                ("Objekt", a.ObjectId is { } o1 ? objectNames.GetValueOrDefault(o1) : null,
                    req.ObjectId is { } o2 ? objectNames.GetValueOrDefault(o2) : null),
                ("Estimat", a.EstimatedHours, req.EstimatedHours ?? a.EstimatedHours),
                ("Status", StatusNames[a.Status], StatusNames[newStatus]),
                ("Start", a.StartDate, req.StartDate),
                ("Slut", a.EndDate, req.EndDate),
                ("Beställare", a.Requester, req.Requester),
                ("Ansvarig", a.Responsible, req.Responsible));
            Apply(a, req);
            a.Status = newStatus;
            a.Priority = req.Priority ?? a.Priority;
            db.Log(user, "Uppdrag", a.Id, "Ändrad", $"Uppdrag {a.Name} ändrades" + (diff.Length > 0 ? $": {diff}" : ""),
                assignmentId: a.Id);
            await db.SaveChangesAsync();
            var totals = await Totals(db, a.Id);
            return Results.Ok(PlanningService.ToDto(a, totals.GetValueOrDefault(a.Id), 0));
        }).RequireAuthorization("Planner");

        g.MapDelete("/{id:int}", async (int id, AppDbContext db, ClaimsPrincipal user) =>
        {
            var a = await db.Assignments.FindAsync(id);
            if (a is null) return Results.NotFound();
            var rows = await db.Allocations.CountAsync(x => x.AssignmentId == id);
            db.Assignments.Remove(a);
            db.Log(user, "Uppdrag", id, "Borttagen",
                $"Uppdrag {a.Name} togs bort" + (rows > 0 ? $" ({rows} resursrader togs också bort)" : ""),
                assignmentId: id);
            await db.SaveChangesAsync();
            return Results.NoContent();
        }).RequireAuthorization("Planner");

        // Ny prioritetsordning i backloggen (drag-n-drop). Skickar alla id:n i önskad ordning.
        g.MapPut("/order", async (int[] ids, AppDbContext db, ClaimsPrincipal user) =>
        {
            var all = await db.Assignments.ToDictionaryAsync(a => a.Id);
            var moved = new List<string>();
            for (var i = 0; i < ids.Length; i++)
            {
                if (!all.TryGetValue(ids[i], out var a)) continue;
                if (a.Priority != i + 1) moved.Add(a.Name);
                a.Priority = i + 1;
            }
            // Uppdrag som inte skickades med hamnar efter, i befintlig ordning.
            var rest = all.Values.Where(a => !ids.Contains(a.Id)).OrderBy(a => a.Priority).ToList();
            for (var i = 0; i < rest.Count; i++) rest[i].Priority = ids.Length + i + 1;

            if (moved.Count > 0)
            {
                var top = ids.Select(id => all.GetValueOrDefault(id)?.Name).Where(n => n is not null).Take(3);
                db.Log(user, "Backlogg", null, "Omprioriterad",
                    $"Backloggen omprioriterades. Topp 3: {string.Join(", ", top)}");
            }
            await db.SaveChangesAsync();
            return Results.NoContent();
        }).RequireAuthorization("Planner");
    }

    private static IResult? Validate(AssignmentRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name)) return Results.Problem("Uppdraget måste ha ett namn.", statusCode: 400);
        if (req.EstimatedHours is < 0 or > 1_000_000) return Results.Problem("Ogiltigt estimat.", statusCode: 400);
        if (req.StartDate is { } s && req.EndDate is { } e && e < s)
            return Results.Problem("Slutdatum kan inte vara före startdatum.", statusCode: 400);
        return null;
    }

    private static void Apply(Assignment a, AssignmentRequest req)
    {
        a.Name = req.Name.Trim();
        a.ObjectId = req.ObjectId;
        a.EstimatedHours = req.EstimatedHours ?? a.EstimatedHours;
        a.StartDate = req.StartDate;
        a.EndDate = req.EndDate;
        a.Requester = Clean(req.Requester);
        a.Responsible = Clean(req.Responsible);
        a.Description = Clean(req.Description);
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    internal static async Task<Dictionary<int, int>> Totals(AppDbContext db, int? assignmentId = null)
    {
        var q = db.AllocationMonths.AsNoTracking().Select(m => new { m.Allocation!.AssignmentId, m.Hours });
        if (assignmentId is { } id) q = q.Where(x => x.AssignmentId == id);
        var rows = await q.ToListAsync();
        return rows.GroupBy(x => x.AssignmentId).ToDictionary(g => g.Key, g => g.Sum(x => x.Hours));
    }
}
