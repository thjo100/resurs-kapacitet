using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ResursKapacitet.Api.Services;

public record ReleaseItem(string Type, string Text);

public record ReleaseNote(string Version, string Date, string Title, List<ReleaseItem> Items);

public record VersionInfo(string Version, string? Commit, string? BuildDate, string? Environment, DateTime StartedAt);

/// <summary>Versionsinformation och release notes ("Nyheter"). Release notes ligger i ReleaseNotes.json och följer med koden.</summary>
public class AppInfo
{
    public IReadOnlyList<ReleaseNote> ReleaseNotes { get; }
    public VersionInfo Version { get; }

    public AppInfo(IConfiguration config)
    {
        ReleaseNotes = LoadReleaseNotes();
        var version = config["APP_VERSION"];
        if (string.IsNullOrWhiteSpace(version)) version = ReleaseNotes.FirstOrDefault()?.Version ?? "0.0.0";
        Version = new VersionInfo(
            version,
            NullIfEmpty(config["GIT_SHA"]),
            NullIfEmpty(config["BUILD_DATE"]),
            NullIfEmpty(config["APP_ENV_LABEL"]),
            DateTime.UtcNow);
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    private static List<ReleaseNote> LoadReleaseNotes()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ReleaseNotes.json");
        if (stream is null) return [];
        return JsonSerializer.Deserialize<List<ReleaseNote>>(stream, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
        }) ?? [];
    }
}
