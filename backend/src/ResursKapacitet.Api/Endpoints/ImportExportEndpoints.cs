using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using ResursKapacitet.Api.Data;
using ResursKapacitet.Api.Services;

namespace ResursKapacitet.Api.Endpoints;

public record ImportRequest(int Year, string Text, bool DryRun = true);

public record ImportResult(
    int Rows, int Cells, int Hours, List<string> NewTeams, List<string> NewResources, List<string> NewObjects,
    List<string> NewAssignments, List<string> Errors, bool Applied);

public static class ImportExportEndpoints
{
    public static void MapImportExportEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/export/planning.csv", async (int? year, PlanningService planning) =>
        {
            var y = year ?? DateTime.Now.Year;
            var p = await planning.BuildAsync(y);
            var sb = new StringBuilder();
            sb.AppendLine("Resurs;Tillhörighet;Uppdrag;Förvaltningsobjekt/Projekt;" +
                          string.Join(';', ChangeLog.MonthNames) + ";Summa");

            var teams = p.Teams.ToDictionary(t => t.Id, t => t.Name);
            var assignments = p.Assignments.ToDictionary(a => a.Id);
            var objects = p.Objects.ToDictionary(o => o.Id, o => o.Name);

            foreach (var r in p.Resources.Where(r => r.IsActive))
            {
                var team = r.TeamId is { } tid ? teams.GetValueOrDefault(tid) : null;
                var rows = p.Allocations.Where(a => a.ResourceId == r.Id).ToList();
                foreach (var a in rows)
                {
                    var asg = assignments.GetValueOrDefault(a.AssignmentId);
                    var obj = asg?.ObjectId is { } oid ? objects.GetValueOrDefault(oid) : null;
                    sb.AppendLine(string.Join(';',
                        new[] { r.Name, team, asg?.Name, obj }.Select(PlanningImport.CsvEscape)
                            .Concat(a.Hours.Select(h => h == 0 ? "" : h.ToString()))
                            .Append(a.Hours.Sum().ToString())));
                }

                var sums = Enumerable.Range(0, 12).Select(m => rows.Sum(a => a.Hours[m])).ToArray();
                sb.AppendLine(string.Join(';',
                    new[] { r.Name, team, "SUMMA", "" }.Select(PlanningImport.CsvEscape)
                        .Concat(sums.Select(h => h.ToString())).Append(sums.Sum().ToString())));
                sb.AppendLine(string.Join(';',
                    new[] { r.Name, team, "KAPACITET", "" }.Select(PlanningImport.CsvEscape)
                        .Concat(r.Capacity.Select(h => h.ToString())).Append(r.Capacity.Sum().ToString())));
                sb.AppendLine();
            }

            return CsvFile(sb, $"planering-{y}.csv");
        });

        app.MapGet("/api/export/assignments.csv", async (int? year, PlanningService planning) =>
        {
            var y = year ?? DateTime.Now.Year;
            var p = await planning.BuildAsync(y);
            var objects = p.Objects.ToDictionary(o => o.Id, o => o.Name);
            var sb = new StringBuilder();
            sb.AppendLine($"Prio;Uppdrag;Förvaltningsobjekt/Projekt;Status;Estimat;Fördelat totalt;Kvar att fördela;Fördelat {y};" +
                          string.Join(';', ChangeLog.MonthNames) + ";Beställare;Ansvarig");
            foreach (var a in p.Assignments)
            {
                var months = Enumerable.Range(0, 12)
                    .Select(m => p.Allocations.Where(x => x.AssignmentId == a.Id).Sum(x => x.Hours[m]));
                sb.AppendLine(string.Join(';', new[]
                    {
                        a.Priority.ToString(), PlanningImport.CsvEscape(a.Name),
                        PlanningImport.CsvEscape(a.ObjectId is { } oid ? objects.GetValueOrDefault(oid) : null),
                        AssignmentEndpoints.StatusNames[a.Status], a.EstimatedHours.ToString(),
                        a.AllocatedTotal.ToString(), a.Remaining.ToString(), a.AllocatedYear.ToString(),
                    }
                    .Concat(months.Select(h => h == 0 ? "" : h.ToString()))
                    .Append(PlanningImport.CsvEscape(a.Requester))
                    .Append(PlanningImport.CsvEscape(a.Responsible))));
            }

            return CsvFile(sb, $"uppdrag-{y}.csv");
        });

        // Hela verksamhetsdatan som JSON (team, resurser, objekt, uppdrag, fördelning). Standard är torrkörning.
        app.MapPost("/api/import/json", async (HttpRequest request, AppDbContext db, ClaimsPrincipal user, bool? dryRun) =>
        {
            Dataset? ds;
            try
            {
                using var reader = new StreamReader(request.Body, Encoding.UTF8);
                ds = JsonImport.Parse(await reader.ReadToEndAsync());
            }
            catch (System.Text.Json.JsonException ex)
            {
                return Results.BadRequest(new { error = $"Ogiltig JSON: {ex.Message}" });
            }
            if (ds is null) return Results.BadRequest(new { error = "Tom JSON." });
            var result = await JsonImport.ApplyAsync(db, ds, dryRun ?? true);
            return Results.Ok(result);
        }).RequireAuthorization("Admin");

        app.MapPost("/api/import/planning", async (ImportRequest req, AppDbContext db, ClaimsPrincipal user) =>
        {
            var parsed = PlanningImport.Parse(req.Text);
            var newTeams = new List<string>();
            var newResources = new List<string>();
            var newObjects = new List<string>();
            var newAssignments = new List<string>();

            if (parsed.Rows.Count == 0)
                return Results.Ok(new ImportResult(0, 0, 0, [], [], [], [], parsed.Errors.DefaultIfEmpty("Inga rader hittades.").ToList(), false));

            var teams = await db.Teams.ToListAsync();
            var resources = await db.Resources.ToListAsync();
            var objects = await db.Objects.ToListAsync();
            var assignments = await db.Assignments.ToListAsync();
            var allocations = await db.Allocations.Include(a => a.Months.Where(m => m.Year == req.Year)).ToListAsync();
            var maxPriority = assignments.Count == 0 ? 0 : assignments.Max(a => a.Priority);

            static bool Same(string a, string? b) => string.Equals(a.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

            var cells = 0;
            var hours = 0;
            var touched = new Dictionary<(Resource, Assignment), int[]>();

            foreach (var row in parsed.Rows)
            {
                Team? team = null;
                if (row.Team is { } tn)
                {
                    team = teams.FirstOrDefault(t => Same(t.Name, tn));
                    if (team is null)
                    {
                        team = new Team { Name = tn, SortOrder = teams.Count + 1 };
                        teams.Add(team);
                        db.Teams.Add(team);
                        newTeams.Add(tn);
                    }
                }

                var resource = resources.FirstOrDefault(r => Same(r.Name, row.Resource));
                if (resource is null)
                {
                    resource = new Resource { Name = row.Resource, Team = team, SortOrder = resources.Count + 1 };
                    resources.Add(resource);
                    db.Resources.Add(resource);
                    newResources.Add(row.Resource);
                }

                WorkObject? obj = null;
                if (row.Object is { } on)
                {
                    obj = objects.FirstOrDefault(o => Same(o.Name, on));
                    if (obj is null)
                    {
                        obj = new WorkObject { Name = on, Type = on.StartsWith("Projekt", StringComparison.OrdinalIgnoreCase) ? ObjectType.Projekt : ObjectType.Forvaltning };
                        objects.Add(obj);
                        db.Objects.Add(obj);
                        newObjects.Add(on);
                    }
                }

                var assignment = assignments.FirstOrDefault(a => Same(a.Name, row.Assignment) &&
                    (obj is null || a.Object == obj || a.ObjectId == obj.Id));
                if (assignment is null)
                {
                    assignment = new Assignment
                    {
                        Name = row.Assignment, Object = obj, Status = AssignmentStatus.Pagaende,
                        Priority = ++maxPriority,
                    };
                    assignments.Add(assignment);
                    db.Assignments.Add(assignment);
                    newAssignments.Add(row.Assignment + (obj is null ? "" : $" ({obj.Name})"));
                }

                var key = (resource, assignment);
                if (!touched.TryGetValue(key, out var sum)) touched[key] = sum = new int[12];
                for (var m = 0; m < 12; m++) sum[m] += row.Hours[m];
            }

            foreach (var ((resource, assignment), sum) in touched)
            {
                var allocation = allocations.FirstOrDefault(a =>
                    (a.Resource == resource || (resource.Id != 0 && a.ResourceId == resource.Id)) &&
                    (a.Assignment == assignment || (assignment.Id != 0 && a.AssignmentId == assignment.Id)));
                if (allocation is null)
                {
                    allocation = new Allocation { Resource = resource, Assignment = assignment };
                    allocations.Add(allocation);
                    db.Allocations.Add(allocation);
                }

                for (var m = 1; m <= 12; m++)
                {
                    var cell = allocation.Months.FirstOrDefault(x => x.Year == req.Year && x.Month == m);
                    var h = sum[m - 1];
                    if ((cell?.Hours ?? 0) == h) continue;
                    cells++;
                    if (cell is null)
                    {
                        cell = new AllocationMonth { Year = req.Year, Month = m };
                        allocation.Months.Add(cell);
                    }
                    cell.Hours = h;
                    cell.UpdatedBy = user.DisplayName();
                    cell.UpdatedAt = DateTime.UtcNow;
                }

                hours += sum.Sum();
                if (assignment.Id == 0 && assignment.EstimatedHours == 0) assignment.EstimatedHours += sum.Sum();
            }

            var result = new ImportResult(parsed.Rows.Count, cells, hours, newTeams, newResources, newObjects,
                newAssignments, parsed.Errors, !req.DryRun);

            if (req.DryRun) return Results.Ok(result);

            db.Log(user, "Import", null, "Importerad",
                $"Import {req.Year}: {parsed.Rows.Count} rader, {cells} celler, {hours} h. " +
                $"Nya: {newResources.Count} resurser, {newTeams.Count} team, {newObjects.Count} objekt, {newAssignments.Count} uppdrag");
            await db.SaveChangesAsync();
            return Results.Ok(result);
        }).RequireAuthorization("Admin");
    }

    private static IResult CsvFile(StringBuilder sb, string fileName)
    {
        // UTF-8 med BOM + semikolon gör att svenska Excel öppnar filen direkt med rätt tecken.
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        return Results.File(bytes, "text/csv; charset=utf-8", fileName);
    }
}
