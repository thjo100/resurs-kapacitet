namespace ResursKapacitet.Api.Data;

public enum ResourceType { Intern, Konsult }

public enum ObjectType { Forvaltning, Projekt }

public enum AssignmentStatus { Backlogg, Planerad, Pagaende, Pausad, Klar }

public enum UserRole { Lasare, Planerare, Admin }

public class Team
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Lead { get; set; }
    public string? Description { get; set; }
    public string Color { get; set; } = "#4f6bed";
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public List<Resource> Resources { get; set; } = [];
}

public class Resource
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int? TeamId { get; set; }
    public Team? Team { get; set; }
    public string? Role { get; set; }
    public ResourceType Type { get; set; } = ResourceType.Intern;
    /// <summary>Kapacitet h/mån vid 100 %. Null = standardkapacitet från inställningar.</summary>
    public int? CapacityPerMonth { get; set; }
    public int EmploymentPercent { get; set; } = 100;
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }
    public int SortOrder { get; set; }
    public List<CapacityOverride> CapacityOverrides { get; set; } = [];
    public List<Allocation> Allocations { get; set; } = [];
}

/// <summary>Avvikande kapacitet en viss månad (semester, föräldraledighet, utbildning…).</summary>
public class CapacityOverride
{
    public int Id { get; set; }
    public int ResourceId { get; set; }
    public Resource? Resource { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public int Hours { get; set; }
    public string? Reason { get; set; }
}

/// <summary>Förvaltningsobjekt eller projekt.</summary>
public class WorkObject
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public ObjectType Type { get; set; } = ObjectType.Forvaltning;
    public string? Owner { get; set; }
    public int? BudgetHoursPerYear { get; set; }
    public string? Description { get; set; }
    public string Color { get; set; } = "#0f9d8a";
    public bool IsActive { get; set; } = true;
    public List<Assignment> Assignments { get; set; } = [];
}

public class Assignment
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int? ObjectId { get; set; }
    public WorkObject? Object { get; set; }
    /// <summary>Totalt estimat i timmar att fördela.</summary>
    public int EstimatedHours { get; set; }
    /// <summary>Rangordning i backloggen (lägre = högre prio).</summary>
    public int Priority { get; set; }
    public AssignmentStatus Status { get; set; } = AssignmentStatus.Backlogg;
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public string? Requester { get; set; }
    public string? Responsible { get; set; }
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<Allocation> Allocations { get; set; } = [];
}

/// <summary>En resurs bemannad på ett uppdrag (en rad i planeringsgriden).</summary>
public class Allocation
{
    public int Id { get; set; }
    public int ResourceId { get; set; }
    public Resource? Resource { get; set; }
    public int AssignmentId { get; set; }
    public Assignment? Assignment { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<AllocationMonth> Months { get; set; } = [];
}

public class AllocationMonth
{
    public int Id { get; set; }
    public int AllocationId { get; set; }
    public Allocation? Allocation { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public int Hours { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string? UpdatedBy { get; set; }
}

public class ChangeLogEntry
{
    public long Id { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string User { get; set; } = "";
    public string EntityType { get; set; } = "";
    public int? EntityId { get; set; }
    public string Action { get; set; } = "";
    public string Summary { get; set; } = "";
    public string? Details { get; set; }
    public int? ResourceId { get; set; }
    public int? AssignmentId { get; set; }
}

public class User
{
    public int Id { get; set; }
    public string UserName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public UserRole Role { get; set; } = UserRole.Lasare;
    public bool IsActive { get; set; } = true;
    public bool MustChangePassword { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }
}

public class AppSettings
{
    public int Id { get; set; } = 1;
    public string OrganizationName { get; set; } = "Systemutveckling";
    public int StandardCapacity { get; set; } = 160;
    /// <summary>Planeringstak i procent av kapacitet (utrymme för möten, kompetensutveckling…).</summary>
    public int PlanningCeilingPercent { get; set; } = 90;
    /// <summary>12 månadsfaktorer, kommaseparerade (JAN..DEC).</summary>
    public string MonthFactors { get; set; } = "1,1,1,1,1,0.9,0.25,0.9,1,1,1,0.85";
    public int ThresholdLow { get; set; } = 80;
    public int ThresholdHigh { get; set; } = 100;
    public int ThresholdCritical { get; set; } = 110;
    public bool RequireLoginToView { get; set; }
    public int ChangeLogRetentionMonths { get; set; } = 24;

    public double[] GetMonthFactors()
    {
        var parts = MonthFactors.Split(',', StringSplitOptions.TrimEntries);
        var result = new double[12];
        for (var i = 0; i < 12; i++)
        {
            result[i] = i < parts.Length && double.TryParse(parts[i], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var f) ? f : 1.0;
        }
        return result;
    }

    public void SetMonthFactors(IEnumerable<double> factors) =>
        MonthFactors = string.Join(',', factors.Take(12).Select(f =>
            Math.Clamp(f, 0, 2).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)));
}
