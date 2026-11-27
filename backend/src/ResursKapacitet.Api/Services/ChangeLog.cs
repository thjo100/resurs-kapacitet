using System.Security.Claims;
using System.Text.Json;
using ResursKapacitet.Api.Data;

namespace ResursKapacitet.Api.Services;

public static class ChangeLog
{
    public static readonly string[] MonthNames =
        ["JAN", "FEB", "MAR", "APR", "MAJ", "JUN", "JUL", "AUG", "SEP", "OKT", "NOV", "DEC"];

    /// <summary>Lägger till en rad i ändringsloggen (sparas vid nästa SaveChanges).</summary>
    public static void Log(this AppDbContext db, ClaimsPrincipal user, string entityType, int? entityId,
        string action, string summary, object? details = null, int? resourceId = null, int? assignmentId = null)
    {
        db.ChangeLog.Add(new ChangeLogEntry
        {
            Timestamp = DateTime.UtcNow,
            User = user.DisplayName(),
            EntityType = entityType,
            EntityId = entityId,
            Action = action,
            Summary = summary.Length > 2000 ? summary[..2000] : summary,
            Details = details is null ? null : JsonSerializer.Serialize(details),
            ResourceId = resourceId,
            AssignmentId = assignmentId,
        });
    }

    /// <summary>Beskriver skillnader mellan två objekt fält för fält (för ändringsloggen).</summary>
    public static string Diff(params (string Label, object? Old, object? New)[] fields)
    {
        var parts = fields
            .Where(f => !Equals(Normalize(f.Old), Normalize(f.New)))
            .Select(f => $"{f.Label}: {Format(f.Old)} → {Format(f.New)}");
        return string.Join(", ", parts);
    }

    private static object? Normalize(object? v) => v is string s && string.IsNullOrWhiteSpace(s) ? null : v;

    private static string Format(object? v) => v switch
    {
        null => "–",
        string s when string.IsNullOrWhiteSpace(s) => "–",
        bool b => b ? "ja" : "nej",
        DateOnly d => d.ToString("yyyy-MM-dd"),
        _ => v.ToString() ?? "–",
    };
}

public static class ClaimsPrincipalExtensions
{
    public const string DisplayNameClaim = "display_name";

    public static string DisplayName(this ClaimsPrincipal user) =>
        user.FindFirstValue(DisplayNameClaim) ?? user.Identity?.Name ?? "system";

    public static int? UserId(this ClaimsPrincipal user) =>
        int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}
