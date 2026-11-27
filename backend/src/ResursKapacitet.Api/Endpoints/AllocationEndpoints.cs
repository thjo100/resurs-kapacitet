using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using ResursKapacitet.Api.Data;
using ResursKapacitet.Api.Services;

namespace ResursKapacitet.Api.Endpoints;

public record CreateAllocationRequest(int ResourceId, int AssignmentId, int? Year, int[]? Hours, string? Note);

public record CellUpdate(int AllocationId, int Month, int Hours, int? Expected);

public record CellsRequest(int Year, List<CellUpdate> Cells, bool Force = false);

public record CellConflict(int AllocationId, int Month, int Current, int Attempted, string? ChangedBy, DateTime? ChangedAt);

public record CellsResponse(List<AllocationDto> Updated, List<CellConflict> Conflicts);

public record MoveAllocationRequest(int ResourceId);

public record NoteRequest(string? Note);

public record CopyYearRequest(int FromYear, int ToYear, bool Overwrite = false);

public static class AllocationEndpoints
{
    public static void MapAllocationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/planning", async (int? year, PlanningService planning, CancellationToken ct) =>
            Results.Ok(await planning.BuildAsync(year ?? DateTime.Now.Year, ct)));

        var g = app.MapGroup("/api/allocations").RequireAuthorization("Planner");

        // Tilldela ett uppdrag till en resurs (t.ex. drag-n-drop från backloggen).
        g.MapPost("/", async (CreateAllocationRequest req, AppDbContext db, ClaimsPrincipal user) =>
        {
            var resource = await db.Resources.FindAsync(req.ResourceId);
            var assignment = await db.Assignments.FindAsync(req.AssignmentId);
            if (resource is null || assignment is null)
                return Results.Problem("Resursen eller uppdraget finns inte.", statusCode: 404);

            var year = req.Year ?? DateTime.Now.Year;
            var allocation = await db.Allocations.Include(a => a.Months)
                .FirstOrDefaultAsync(a => a.ResourceId == req.ResourceId && a.AssignmentId == req.AssignmentId);
            var isNew = allocation is null;
            allocation ??= db.Allocations.Add(new Allocation
            {
                ResourceId = req.ResourceId, AssignmentId = req.AssignmentId, Note = req.Note,
            }).Entity;

            var total = 0;
            if (req.Hours is { Length: > 0 } hours)
            {
                for (var m = 1; m <= Math.Min(12, hours.Length); m++)
                {
                    var h = Math.Clamp(hours[m - 1], 0, 744);
                    if (h == 0) continue;
                    var cell = allocation.Months.FirstOrDefault(x => x.Year == year && x.Month == m);
                    if (cell is null)
                    {
                        cell = new AllocationMonth { Year = year, Month = m };
                        allocation.Months.Add(cell);
                    }
                    cell.Hours += h;
                    cell.UpdatedAt = DateTime.UtcNow;
                    cell.UpdatedBy = user.DisplayName();
                    total += h;
                }
            }

            var statusNote = "";
            if (assignment.Status == AssignmentStatus.Backlogg)
            {
                assignment.Status = AssignmentStatus.Planerad;
                statusNote = " – uppdraget flyttades från backloggen till Planerad";
            }

            db.Log(user, "Allokering", null, isNew ? "Tilldelad" : "Ändrad",
                $"{assignment.Name} tilldelades {resource.Name}" + (total > 0 ? $" ({total} h {year})" : "") + statusNote,
                resourceId: resource.Id, assignmentId: assignment.Id);
            await db.SaveChangesAsync();
            return Results.Ok(await ToDto(db, allocation.Id, year));
        });

        // Spara en eller flera celler (som i Excel). Optimistisk låsning: klienten skickar förväntat värde.
        g.MapPut("/cells", async (CellsRequest req, AppDbContext db, ClaimsPrincipal user) =>
        {
            if (req.Cells.Count == 0) return Results.Ok(new CellsResponse([], []));
            if (req.Cells.Any(c => c.Month is < 1 or > 12))
                return Results.Problem("Ogiltig månad.", statusCode: 400);
            if (req.Cells.Any(c => c.Hours is < 0 or > 744))
                return Results.Problem("Timmar måste vara mellan 0 och 744.", statusCode: 400);

            var ids = req.Cells.Select(c => c.AllocationId).Distinct().ToList();
            var allocations = await db.Allocations
                .Include(a => a.Resource).Include(a => a.Assignment)
                .Include(a => a.Months.Where(m => m.Year == req.Year))
                .Where(a => ids.Contains(a.Id))
                .ToDictionaryAsync(a => a.Id);

            var conflicts = new List<CellConflict>();
            var changes = new Dictionary<int, List<string>>();
            var userName = user.DisplayName();

            foreach (var c in req.Cells)
            {
                if (!allocations.TryGetValue(c.AllocationId, out var allocation)) continue;
                var cell = allocation.Months.FirstOrDefault(m => m.Month == c.Month);
                var current = cell?.Hours ?? 0;

                if (!req.Force && c.Expected is { } expected && expected != current && current != c.Hours)
                {
                    conflicts.Add(new CellConflict(c.AllocationId, c.Month, current, c.Hours, cell?.UpdatedBy, cell?.UpdatedAt));
                    continue;
                }

                if (current == c.Hours) continue;

                if (cell is null)
                {
                    cell = new AllocationMonth { AllocationId = allocation.Id, Year = req.Year, Month = c.Month };
                    allocation.Months.Add(cell);
                }

                cell.Hours = c.Hours;
                cell.UpdatedAt = DateTime.UtcNow;
                cell.UpdatedBy = userName;

                if (!changes.TryGetValue(allocation.Id, out var list)) changes[allocation.Id] = list = [];
                list.Add($"{ChangeLog.MonthNames[c.Month - 1]} {current}→{c.Hours}");
            }

            foreach (var (allocationId, list) in changes)
            {
                var a = allocations[allocationId];
                if (a.Assignment is { Status: AssignmentStatus.Backlogg } asg) asg.Status = AssignmentStatus.Planerad;
                db.Log(user, "Allokering", a.Id, "Ändrad",
                    $"{a.Resource?.Name} · {a.Assignment?.Name} ({req.Year}): {string.Join(", ", list)}",
                    resourceId: a.ResourceId, assignmentId: a.AssignmentId);
            }

            await db.SaveChangesAsync();

            var updated = new List<AllocationDto>();
            foreach (var id in ids.Where(allocations.ContainsKey))
                if (await ToDto(db, id, req.Year) is { } dto) updated.Add(dto);
            return Results.Ok(new CellsResponse(updated, conflicts));
        });

        // Flytta en uppdragsrad till en annan resurs. Finns uppdraget redan där slås timmarna ihop.
        g.MapPut("/{id:int}/move", async (int id, MoveAllocationRequest req, AppDbContext db, ClaimsPrincipal user) =>
        {
            var source = await db.Allocations.Include(a => a.Months).Include(a => a.Resource).Include(a => a.Assignment)
                .FirstOrDefaultAsync(a => a.Id == id);
            var target = await db.Resources.FindAsync(req.ResourceId);
            if (source is null || target is null) return Results.NotFound();
            if (source.ResourceId == target.Id) return Results.Ok(new { id = source.Id });

            var existing = await db.Allocations.Include(a => a.Months)
                .FirstOrDefaultAsync(a => a.ResourceId == target.Id && a.AssignmentId == source.AssignmentId);
            var fromName = source.Resource?.Name;
            int resultId;
            if (existing is null)
            {
                source.ResourceId = target.Id;
                resultId = source.Id;
            }
            else
            {
                foreach (var m in source.Months)
                {
                    var t = existing.Months.FirstOrDefault(x => x.Year == m.Year && x.Month == m.Month);
                    if (t is null) existing.Months.Add(new AllocationMonth
                        { Year = m.Year, Month = m.Month, Hours = m.Hours, UpdatedBy = user.DisplayName() });
                    else t.Hours += m.Hours;
                }
                db.Allocations.Remove(source);
                resultId = existing.Id;
            }

            db.Log(user, "Allokering", resultId, "Flyttad",
                $"{source.Assignment?.Name} flyttades från {fromName} till {target.Name}" +
                (existing is null ? "" : " (timmarna slogs ihop med befintlig rad)"),
                resourceId: target.Id, assignmentId: source.AssignmentId);
            await db.SaveChangesAsync();
            return Results.Ok(new { id = resultId });
        });

        g.MapPut("/{id:int}/note", async (int id, NoteRequest req, AppDbContext db, ClaimsPrincipal user) =>
        {
            var a = await db.Allocations.Include(x => x.Resource).Include(x => x.Assignment).FirstOrDefaultAsync(x => x.Id == id);
            if (a is null) return Results.NotFound();
            a.Note = string.IsNullOrWhiteSpace(req.Note) ? null : req.Note.Trim();
            db.Log(user, "Allokering", id, "Ändrad", $"{a.Resource?.Name} · {a.Assignment?.Name}: kommentar ändrad",
                resourceId: a.ResourceId, assignmentId: a.AssignmentId);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        g.MapDelete("/{id:int}", async (int id, AppDbContext db, ClaimsPrincipal user) =>
        {
            var a = await db.Allocations.Include(x => x.Resource).Include(x => x.Assignment).Include(x => x.Months)
                .FirstOrDefaultAsync(x => x.Id == id);
            if (a is null) return Results.NotFound();
            var hours = a.Months.Sum(m => m.Hours);
            db.Allocations.Remove(a);
            db.Log(user, "Allokering", id, "Borttagen",
                $"{a.Assignment?.Name} togs bort från {a.Resource?.Name}" + (hours > 0 ? $" ({hours} h totalt)" : ""),
                resourceId: a.ResourceId, assignmentId: a.AssignmentId);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        // Kopiera ett års plan till ett annat år (t.ex. inför nästa år).
        g.MapPost("/copy-year", async (CopyYearRequest req, AppDbContext db, ClaimsPrincipal user) =>
        {
            if (req.FromYear == req.ToYear) return Results.Problem("Välj två olika år.", statusCode: 400);
            var source = await db.AllocationMonths.Where(m => m.Year == req.FromYear && m.Hours > 0).ToListAsync();
            var target = await db.AllocationMonths.Where(m => m.Year == req.ToYear).ToListAsync();
            if (target.Any(t => t.Hours > 0) && !req.Overwrite)
                return Results.Problem($"{req.ToYear} har redan planerade timmar. Välj att skriva över om du vill fortsätta.",
                    statusCode: 409);
            db.AllocationMonths.RemoveRange(target);
            foreach (var m in source)
                db.AllocationMonths.Add(new AllocationMonth
                {
                    AllocationId = m.AllocationId, Year = req.ToYear, Month = m.Month, Hours = m.Hours,
                    UpdatedBy = user.DisplayName(),
                });
            db.Log(user, "Planering", null, "Kopierad",
                $"Planen för {req.FromYear} kopierades till {req.ToYear} ({source.Sum(s => s.Hours)} h)");
            await db.SaveChangesAsync();
            return Results.Ok(new { copiedHours = source.Sum(s => s.Hours) });
        });
    }

    internal static async Task<AllocationDto?> ToDto(AppDbContext db, int allocationId, int year)
    {
        var a = await db.Allocations.AsNoTracking().FirstOrDefaultAsync(x => x.Id == allocationId);
        if (a is null) return null;
        var months = await db.AllocationMonths.AsNoTracking().Where(m => m.AllocationId == allocationId).ToListAsync();
        var hours = new int[12];
        foreach (var m in months.Where(m => m.Year == year)) hours[m.Month - 1] = m.Hours;
        return new AllocationDto(a.Id, a.ResourceId, a.AssignmentId, a.Note, hours,
            months.Where(m => m.Year != year).Sum(m => m.Hours));
    }
}
