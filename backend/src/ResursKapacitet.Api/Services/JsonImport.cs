using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ResursKapacitet.Api.Data;

namespace ResursKapacitet.Api.Services;

public record DatasetSettings(string? OrganizationName, int? StandardCapacity, int? PlanningCeilingPercent, double[]? MonthFactors);
public record DatasetTeam(string Name, string? Lead, string? Description, string? Color);
public record DatasetAbsence(int Month, int Hours, string? Reason, int? Year);
public record DatasetResource(
    string Name, string? Team, string? Role, string? Type, int? CapacityPerMonth, int? EmploymentPercent,
    string? StartDate, string? EndDate, string? Notes, List<DatasetAbsence>? Absence);
public record DatasetObject(string Name, string? Type, string? Owner, int? BudgetHoursPerYear, string? Description, string? Color);
public record DatasetAssignment(
    string Name, string? Object, int? EstimatedHours, int? Priority, string? Status, string? StartDate,
    string? EndDate, string? Requester, string? Responsible, string? Description);
public record DatasetAllocation(string Resource, string Assignment, string? Object, int? Year, int[] Hours, string? Note);

/// <summary>Hela verksamhetsdatan i ett JSON-dokument. Alla delar är valfria.</summary>
public record Dataset(
    int? Year, DatasetSettings? Settings, List<DatasetTeam>? Teams, List<DatasetResource>? Resources,
    List<DatasetObject>? Objects, List<DatasetAssignment>? Assignments, List<DatasetAllocation>? Allocations);

public record DatasetImportResult(
    bool Applied, int Teams, int Resources, int Objects, int Assignments, int Allocations,
    int NewTeams, int NewResources, int NewObjects, int NewAssignments, int NewAllocations, int Hours,
    List<string> Errors);

/// <summary>
/// Läser in verksamhetsdata från JSON. Matchar på namn (skiftlägesokänsligt): finns posten uppdateras den,
/// annars skapas den. Inget raderas. Timmar för en fördelning ersätts för angivet år.
/// </summary>
public static class JsonImport
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static Dataset? Parse(string json) => JsonSerializer.Deserialize<Dataset>(json, Options);

    public static async Task<DatasetImportResult> ApplyAsync(AppDbContext db, Dataset ds, bool dryRun)
    {
        var errors = new List<string>();
        var year = ds.Year ?? DateTime.Now.Year;
        int nTeams = 0, nRes = 0, nObj = 0, nAsg = 0, nAlloc = 0, hours = 0;

        await using var tx = await db.Database.BeginTransactionAsync();

        static bool Same(string? a, string? b) => string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

        // ---- Inställningar ----
        if (ds.Settings is { } st)
        {
            var s = await db.Settings.FirstAsync();
            if (!string.IsNullOrWhiteSpace(st.OrganizationName)) s.OrganizationName = st.OrganizationName.Trim();
            if (st.StandardCapacity is { } sc) s.StandardCapacity = Math.Clamp(sc, 0, 400);
            if (st.PlanningCeilingPercent is { } pc) s.PlanningCeilingPercent = Math.Clamp(pc, 0, 200);
            if (st.MonthFactors is { Length: 12 } mf) s.SetMonthFactors(mf);
            else if (st.MonthFactors is not null) errors.Add("settings.monthFactors måste ha exakt 12 värden.");
        }

        var teams = await db.Teams.ToListAsync();
        var resources = await db.Resources.Include(r => r.CapacityOverrides).ToListAsync();
        var objects = await db.Objects.ToListAsync();
        var assignments = await db.Assignments.ToListAsync();
        var allocations = await db.Allocations.Include(a => a.Months).ToListAsync();

        // ---- Team ----
        foreach (var t in ds.Teams ?? [])
        {
            if (string.IsNullOrWhiteSpace(t.Name)) { errors.Add("Team utan namn hoppades över."); continue; }
            var team = teams.FirstOrDefault(x => Same(x.Name, t.Name));
            if (team is null)
            {
                team = new Team { Name = t.Name.Trim(), SortOrder = teams.Count + 1 };
                teams.Add(team);
                db.Teams.Add(team);
                nTeams++;
            }
            if (t.Lead is not null) team.Lead = t.Lead;
            if (t.Description is not null) team.Description = t.Description;
            if (!string.IsNullOrWhiteSpace(t.Color)) team.Color = t.Color;
        }

        // ---- Objekt ----
        foreach (var o in ds.Objects ?? [])
        {
            if (string.IsNullOrWhiteSpace(o.Name)) { errors.Add("Objekt utan namn hoppades över."); continue; }
            var obj = objects.FirstOrDefault(x => Same(x.Name, o.Name));
            if (obj is null)
            {
                obj = new WorkObject { Name = o.Name.Trim() };
                objects.Add(obj);
                db.Objects.Add(obj);
                nObj++;
            }
            if (o.Type is not null) obj.Type = ParseEnum(o.Type, obj.Type, errors, $"objekt '{o.Name}' type");
            if (o.Owner is not null) obj.Owner = o.Owner;
            if (o.BudgetHoursPerYear is { } b) obj.BudgetHoursPerYear = b;
            if (o.Description is not null) obj.Description = o.Description;
            if (!string.IsNullOrWhiteSpace(o.Color)) obj.Color = o.Color;
        }

        // ---- Resurser ----
        foreach (var r in ds.Resources ?? [])
        {
            if (string.IsNullOrWhiteSpace(r.Name)) { errors.Add("Resurs utan namn hoppades över."); continue; }
            var res = resources.FirstOrDefault(x => Same(x.Name, r.Name));
            if (res is null)
            {
                res = new Resource { Name = r.Name.Trim(), SortOrder = resources.Count + 1 };
                resources.Add(res);
                db.Resources.Add(res);
                nRes++;
            }
            if (!string.IsNullOrWhiteSpace(r.Team))
            {
                var team = teams.FirstOrDefault(x => Same(x.Name, r.Team));
                if (team is null)
                {
                    team = new Team { Name = r.Team.Trim(), SortOrder = teams.Count + 1 };
                    teams.Add(team);
                    db.Teams.Add(team);
                    nTeams++;
                }
                res.Team = team;
            }
            if (r.Role is not null) res.Role = r.Role;
            if (r.Type is not null) res.Type = ParseEnum(r.Type, res.Type, errors, $"resurs '{r.Name}' type");
            if (r.CapacityPerMonth is { } c) res.CapacityPerMonth = c;
            if (r.EmploymentPercent is { } e) res.EmploymentPercent = Math.Clamp(e, 0, 100);
            if (ParseDate(r.StartDate, errors, $"resurs '{r.Name}' startDate") is { } sd) res.StartDate = sd;
            if (ParseDate(r.EndDate, errors, $"resurs '{r.Name}' endDate") is { } ed) res.EndDate = ed;
            if (r.Notes is not null) res.Notes = r.Notes;

            foreach (var a in r.Absence ?? [])
            {
                if (a.Month is < 1 or > 12) { errors.Add($"Resurs '{r.Name}': ogiltig månad {a.Month} i absence."); continue; }
                var y = a.Year ?? year;
                var o = res.CapacityOverrides.FirstOrDefault(x => x.Year == y && x.Month == a.Month);
                if (o is null)
                {
                    o = new CapacityOverride { Year = y, Month = a.Month };
                    res.CapacityOverrides.Add(o);
                }
                o.Hours = Math.Max(0, a.Hours);
                if (a.Reason is not null) o.Reason = a.Reason;
            }
        }

        // ---- Uppdrag ----
        var maxPriority = assignments.Count == 0 ? 0 : assignments.Max(a => a.Priority);
        WorkObject? FindObject(string? name) => string.IsNullOrWhiteSpace(name) ? null : objects.FirstOrDefault(x => Same(x.Name, name));
        Assignment? FindAssignment(string name, string? objName)
        {
            var obj = FindObject(objName);
            return assignments.FirstOrDefault(a => Same(a.Name, name) &&
                (string.IsNullOrWhiteSpace(objName) || (obj is not null && (a.Object == obj || (obj.Id != 0 && a.ObjectId == obj.Id)))));
        }

        foreach (var a in ds.Assignments ?? [])
        {
            if (string.IsNullOrWhiteSpace(a.Name)) { errors.Add("Uppdrag utan namn hoppades över."); continue; }
            WorkObject? obj = null;
            if (!string.IsNullOrWhiteSpace(a.Object))
            {
                obj = FindObject(a.Object);
                if (obj is null)
                {
                    obj = new WorkObject
                    {
                        Name = a.Object.Trim(),
                        Type = a.Object.StartsWith("Projekt", StringComparison.OrdinalIgnoreCase) ? ObjectType.Projekt : ObjectType.Forvaltning,
                    };
                    objects.Add(obj);
                    db.Objects.Add(obj);
                    nObj++;
                }
            }
            var asg = FindAssignment(a.Name, a.Object);
            if (asg is null)
            {
                asg = new Assignment { Name = a.Name.Trim(), Priority = ++maxPriority };
                assignments.Add(asg);
                db.Assignments.Add(asg);
                nAsg++;
            }
            if (obj is not null) asg.Object = obj;
            if (a.EstimatedHours is { } eh) asg.EstimatedHours = Math.Max(0, eh);
            if (a.Priority is { } p) asg.Priority = p;
            if (a.Status is not null) asg.Status = ParseEnum(a.Status, asg.Status, errors, $"uppdrag '{a.Name}' status");
            if (ParseDate(a.StartDate, errors, $"uppdrag '{a.Name}' startDate") is { } sd) asg.StartDate = sd;
            if (ParseDate(a.EndDate, errors, $"uppdrag '{a.Name}' endDate") is { } ed) asg.EndDate = ed;
            if (a.Requester is not null) asg.Requester = a.Requester;
            if (a.Responsible is not null) asg.Responsible = a.Responsible;
            if (a.Description is not null) asg.Description = a.Description;
        }

        // ---- Fördelningar (timmar per månad) ----
        foreach (var f in ds.Allocations ?? [])
        {
            var label = $"fördelning {f.Resource} → {f.Assignment}";
            var res = resources.FirstOrDefault(x => Same(x.Name, f.Resource));
            var asg = FindAssignment(f.Assignment, f.Object);
            if (res is null) { errors.Add($"{label}: resursen finns inte (lägg den under \"resources\")."); continue; }
            if (asg is null) { errors.Add($"{label}: uppdraget finns inte (lägg det under \"assignments\")."); continue; }
            if (f.Hours is not { Length: 12 }) { errors.Add($"{label}: hours måste ha exakt 12 värden (JAN–DEC)."); continue; }
            if (f.Hours.Any(h => h < 0)) { errors.Add($"{label}: negativa timmar är inte tillåtna."); continue; }

            var y = f.Year ?? year;
            var alloc = allocations.FirstOrDefault(x =>
                (x.Resource == res || (res.Id != 0 && x.ResourceId == res.Id)) &&
                (x.Assignment == asg || (asg.Id != 0 && x.AssignmentId == asg.Id)));
            if (alloc is null)
            {
                alloc = new Allocation { Resource = res, Assignment = asg };
                allocations.Add(alloc);
                db.Allocations.Add(alloc);
                nAlloc++;
            }
            if (f.Note is not null) alloc.Note = f.Note;
            for (var m = 1; m <= 12; m++)
            {
                var cell = alloc.Months.FirstOrDefault(x => x.Year == y && x.Month == m);
                if (cell is null)
                {
                    if (f.Hours[m - 1] == 0) continue;
                    cell = new AllocationMonth { Year = y, Month = m };
                    alloc.Months.Add(cell);
                }
                cell.Hours = f.Hours[m - 1];
                cell.UpdatedBy = "Import";
                cell.UpdatedAt = DateTime.UtcNow;
            }
            hours += f.Hours.Sum();
        }

        // Importen är allt-eller-inget: vid fel eller torrkörning rullas allt tillbaka.
        var apply = !dryRun && errors.Count == 0;
        if (apply)
        {
            db.ChangeLog.Add(new ChangeLogEntry
            {
                User = "Import", EntityType = "Import", Action = "Importerad",
                Summary = $"JSON-import {year}: {nTeams} nya team, {nRes} nya resurser, {nObj} nya objekt, " +
                          $"{nAsg} nya uppdrag, {ds.Allocations?.Count ?? 0} fördelningar, {hours} h",
            });
            await db.SaveChangesAsync();
            await tx.CommitAsync();
        }
        else
        {
            await db.SaveChangesAsync();   // ger id:n/kontroller, rullas tillbaka nedan
            await tx.RollbackAsync();
        }

        return new DatasetImportResult(apply,
            ds.Teams?.Count ?? 0, ds.Resources?.Count ?? 0, ds.Objects?.Count ?? 0, ds.Assignments?.Count ?? 0,
            ds.Allocations?.Count ?? 0, nTeams, nRes, nObj, nAsg, nAlloc, hours, errors);
    }

    private static string Norm(string s) => new string(s.Normalize(NormalizationForm.FormD)
        .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray()).ToLowerInvariant();

    /// <summary>Tolerant enum-tolkning: ignorerar skiftläge och å/ä/ö ("Pågående" = Pagaende, "Förvaltning" = Forvaltning).</summary>
    private static T ParseEnum<T>(string value, T fallback, List<string> errors, string label) where T : struct, Enum
    {
        foreach (var n in Enum.GetNames<T>())
            if (Norm(n) == Norm(value.Trim())) return Enum.Parse<T>(n);
        errors.Add($"{label}: okänt värde '{value}'. Tillåtna: {string.Join(", ", Enum.GetNames<T>())}.");
        return fallback;
    }

    private static DateOnly? ParseDate(string? s, List<string> errors, string label)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        if (DateOnly.TryParseExact(s.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return d;
        errors.Add($"{label}: '{s}' är inget datum (använd ÅÅÅÅ-MM-DD).");
        return null;
    }
}
