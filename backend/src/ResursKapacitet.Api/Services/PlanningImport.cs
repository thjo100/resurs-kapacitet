using System.Globalization;

namespace ResursKapacitet.Api.Services;

public record ImportRow(int Line, string Resource, string? Team, string Assignment, string? Object, int[] Hours);

public record ParseResult(List<ImportRow> Rows, List<string> Errors);

/// <summary>
/// Tolkar text inklistrad från Excel (tabbseparerad) eller CSV (semikolon/komma) i formatet
/// Resurs | Tillhörighet | Uppdrag | Förvaltningsobjekt/Projekt | JAN..DEC.
/// </summary>
public static class PlanningImport
{
    private static readonly string[] Months =
        ["jan", "feb", "mar", "apr", "maj", "jun", "jul", "aug", "sep", "okt", "nov", "dec"];

    public static ParseResult Parse(string text)
    {
        var rows = new List<ImportRow>();
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return new ParseResult(rows, ["Ingen data att importera."]);

        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var delimiter = DetectDelimiter(lines);

        int? colResource = null, colTeam = null, colAssignment = null, colObject = null;
        var colMonths = new int?[12];
        var headerFound = false;

        for (var i = 0; i < lines.Length; i++)
        {
            var cells = SplitLine(lines[i], delimiter);
            if (cells.All(string.IsNullOrWhiteSpace)) continue;

            if (!headerFound)
            {
                var idx = cells.FindIndex(c => c.Trim().Equals("resurs", StringComparison.OrdinalIgnoreCase));
                if (idx >= 0)
                {
                    headerFound = true;
                    for (var c = 0; c < cells.Count; c++)
                    {
                        var h = cells[c].Trim().ToLowerInvariant();
                        if (h == "resurs") colResource = c;
                        else if (h is "tillhörighet" or "team") colTeam = c;
                        else if (h == "uppdrag") colAssignment = c;
                        else if (h.StartsWith("förvaltning") || h.StartsWith("objekt") || h.StartsWith("projekt")) colObject = c;
                        else
                        {
                            var m = Array.FindIndex(Months, mn => h.StartsWith(mn));
                            if (m >= 0 && h.Length <= 9) colMonths[m] = c;
                        }
                    }
                    if (colAssignment is null) errors.Add("Hittade ingen kolumn 'Uppdrag' i rubrikraden.");
                    continue;
                }

                // Ingen rubrikrad: anta fasta positioner från första icke-tomma kolumnen.
                headerFound = true;
                var start = cells.FindIndex(c => !string.IsNullOrWhiteSpace(c));
                colResource = start;
                colTeam = start + 1;
                colAssignment = start + 2;
                colObject = start + 3;
                for (var m = 0; m < 12; m++) colMonths[m] = start + 4 + m;
            }

            string Get(int? col) => col is { } c && c < cells.Count ? cells[c].Trim() : "";

            var resource = Get(colResource);
            var assignment = Get(colAssignment);
            if (resource.Length == 0 && assignment.Length == 0) continue;
            if (resource.Length == 0)
            {
                errors.Add($"Rad {i + 1}: resurs saknas.");
                continue;
            }
            if (assignment.Length == 0)
            {
                errors.Add($"Rad {i + 1}: uppdrag saknas för {resource}.");
                continue;
            }

            var hours = new int[12];
            var rowOk = true;
            for (var m = 0; m < 12; m++)
            {
                var raw = Get(colMonths[m]).Replace(" ", "").Replace(" ", "");
                if (raw.Length == 0) continue;
                if (double.TryParse(raw.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) &&
                    v >= 0 && v <= 744)
                {
                    hours[m] = (int)Math.Round(v, MidpointRounding.AwayFromZero);
                }
                else
                {
                    errors.Add($"Rad {i + 1}: ogiltigt värde '{raw}' i {Months[m].ToUpperInvariant()}.");
                    rowOk = false;
                }
            }

            if (!rowOk) continue;
            rows.Add(new ImportRow(i + 1, resource, NullIfEmpty(Get(colTeam)), assignment, NullIfEmpty(Get(colObject)), hours));
        }

        return new ParseResult(rows, errors);
    }

    private static string? NullIfEmpty(string s) => s.Length == 0 ? null : s;

    private static char DetectDelimiter(string[] lines)
    {
        var sample = string.Join('\n', lines.Take(20));
        if (sample.Contains('\t')) return '\t';
        return sample.Count(c => c == ';') >= sample.Count(c => c == ',') ? ';' : ',';
    }

    private static List<string> SplitLine(string line, char delimiter)
    {
        var result = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else inQuotes = !inQuotes;
            }
            else if (ch == delimiter && !inQuotes)
            {
                result.Add(current.ToString());
                current.Clear();
            }
            else current.Append(ch);
        }
        result.Add(current.ToString());
        return result;
    }

    public static string CsvEscape(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.IndexOfAny([';', '"', '\n', '\r']) >= 0 ? $"\"{s.Replace("\"", "\"\"")}\"" : s;
    }
}
