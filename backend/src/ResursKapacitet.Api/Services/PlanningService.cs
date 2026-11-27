using Microsoft.EntityFrameworkCore;
using ResursKapacitet.Api.Data;

namespace ResursKapacitet.Api.Services;

public record SettingsDto(
    string OrganizationName,
    int StandardCapacity,
    int PlanningCeilingPercent,
    double[] MonthFactors,
    int ThresholdLow,
    int ThresholdHigh,
    int ThresholdCritical,
    bool RequireLoginToView,
    int ChangeLogRetentionMonths)
{
    public static SettingsDto From(AppSettings s) => new(s.OrganizationName, s.StandardCapacity,
        s.PlanningCeilingPercent, s.GetMonthFactors(), s.ThresholdLow, s.ThresholdHigh, s.ThresholdCritical,
        s.RequireLoginToView, s.ChangeLogRetentionMonths);
}

public record TeamDto(int Id, string Name, string? Lead, string? Description, string Color, int SortOrder, bool IsActive)
{
    public static TeamDto From(Team t) => new(t.Id, t.Name, t.Lead, t.Description, t.Color, t.SortOrder, t.IsActive);
}

public record AbsenceDto(int Month, int Hours, string? Reason);

public record ResourceDto(
    int Id, string Name, int? TeamId, string? Role, ResourceType Type, int? CapacityPerMonth,
    int EmploymentPercent, DateOnly? StartDate, DateOnly? EndDate, bool IsActive, string? Notes, int SortOrder,
    int[] Capacity, List<AbsenceDto> Absences)
{
    public static ResourceDto From(Resource r, int[] capacity, List<AbsenceDto> absences) => new(r.Id, r.Name,
        r.TeamId, r.Role, r.Type, r.CapacityPerMonth, r.EmploymentPercent, r.StartDate, r.EndDate, r.IsActive,
        r.Notes, r.SortOrder, capacity, absences);
}

public record ObjectDto(int Id, string Name, ObjectType Type, string? Owner, int? BudgetHoursPerYear,
    string? Description, string Color, bool IsActive)
{
    public static ObjectDto From(WorkObject o) => new(o.Id, o.Name, o.Type, o.Owner, o.BudgetHoursPerYear,
        o.Description, o.Color, o.IsActive);
}

public record AssignmentDto(
    int Id, string Name, int? ObjectId, int EstimatedHours, int Priority, AssignmentStatus Status,
    DateOnly? StartDate, DateOnly? EndDate, string? Requester, string? Responsible, string? Description,
    DateTime CreatedAt, int AllocatedTotal, int AllocatedYear)
{
    public int Remaining => EstimatedHours - AllocatedTotal;
}

public record AllocationDto(int Id, int ResourceId, int AssignmentId, string? Note, int[] Hours, int OtherYearsHours);

public record PlanningDto(
    int Year,
    SettingsDto Settings,
    List<TeamDto> Teams,
    List<ResourceDto> Resources,
    List<ObjectDto> Objects,
    List<AssignmentDto> Assignments,
    List<AllocationDto> Allocations,
    long LastChangeId);

public class PlanningService(AppDbContext db, SettingsStore settingsStore)
{
    public async Task<PlanningDto> BuildAsync(int year, CancellationToken ct = default)
    {
        var settings = await settingsStore.GetAsync(ct);

        var teams = await db.Teams.AsNoTracking().OrderBy(t => t.SortOrder).ThenBy(t => t.Name).ToListAsync(ct);
        var resources = await db.Resources.AsNoTracking().OrderBy(r => r.SortOrder).ThenBy(r => r.Name)
            .ToListAsync(ct);
        var overrides = await db.CapacityOverrides.AsNoTracking().Where(o => o.Year == year).ToListAsync(ct);
        var objects = await db.Objects.AsNoTracking().OrderBy(o => o.Name).ToListAsync(ct);
        var assignments = await db.Assignments.AsNoTracking().OrderBy(a => a.Priority).ThenBy(a => a.Name)
            .ToListAsync(ct);
        var allocations = await db.Allocations.AsNoTracking().ToListAsync(ct);
        var months = await db.AllocationMonths.AsNoTracking()
            .Select(m => new { m.AllocationId, m.Year, m.Month, m.Hours })
            .ToListAsync(ct);

        var allocationAssignment = allocations.ToDictionary(a => a.Id, a => a.AssignmentId);
        var totalByAssignment = months
            .GroupBy(m => allocationAssignment.GetValueOrDefault(m.AllocationId))
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Hours));
        var yearByAssignment = months.Where(m => m.Year == year)
            .GroupBy(m => allocationAssignment.GetValueOrDefault(m.AllocationId))
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Hours));

        var assignmentById = assignments.ToDictionary(a => a.Id);
        var monthsByAllocation = months.GroupBy(m => m.AllocationId).ToDictionary(g => g.Key, g => g.ToList());

        var allocationDtos = new List<AllocationDto>();
        foreach (var a in allocations)
        {
            var hours = new int[12];
            var other = 0;
            if (monthsByAllocation.TryGetValue(a.Id, out var list))
            {
                foreach (var m in list)
                {
                    if (m.Year == year) hours[m.Month - 1] = m.Hours;
                    else other += m.Hours;
                }
            }

            var hasYearHours = hours.Any(h => h != 0);
            if (!hasYearHours && assignmentById.TryGetValue(a.AssignmentId, out var asg) && !IsRelevantForYear(asg, year, other))
                continue;

            allocationDtos.Add(new AllocationDto(a.Id, a.ResourceId, a.AssignmentId, a.Note, hours, other));
        }

        var lastChangeId = await db.ChangeLog.AsNoTracking().OrderByDescending(c => c.Id).Select(c => c.Id)
            .FirstOrDefaultAsync(ct);

        return new PlanningDto(
            year,
            SettingsDto.From(settings),
            teams.Select(TeamDto.From).ToList(),
            resources.Select(r => ResourceDto.From(r,
                CapacityCalculator.ForYear(r, settings, year, overrides),
                overrides.Where(o => o.ResourceId == r.Id)
                    .OrderBy(o => o.Month)
                    .Select(o => new AbsenceDto(o.Month, o.Hours, o.Reason)).ToList())).ToList(),
            objects.Select(ObjectDto.From).ToList(),
            assignments.Select(a => ToDto(a, totalByAssignment.GetValueOrDefault(a.Id),
                yearByAssignment.GetValueOrDefault(a.Id))).ToList(),
            allocationDtos,
            lastChangeId);
    }

    /// <summary>En rad utan timmar visas för ett år om uppdraget är aktivt och dess period omfattar året.</summary>
    private static bool IsRelevantForYear(Assignment a, int year, int otherYearsHours)
    {
        if (a.Status == AssignmentStatus.Klar) return false;
        if (a.StartDate is { } s && s.Year > year) return false;
        if (a.EndDate is { } e && e.Year < year) return false;
        // Rader som bara har timmar andra år och saknar period visas inte.
        if (otherYearsHours > 0 && a.StartDate is null && a.EndDate is null) return false;
        return true;
    }

    public static AssignmentDto ToDto(Assignment a, int allocatedTotal, int allocatedYear) => new(a.Id, a.Name,
        a.ObjectId, a.EstimatedHours, a.Priority, a.Status, a.StartDate, a.EndDate, a.Requester, a.Responsible,
        a.Description, a.CreatedAt, allocatedTotal, allocatedYear);
}
