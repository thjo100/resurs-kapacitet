using ResursKapacitet.Api.Data;
using ResursKapacitet.Api.Services;

namespace ResursKapacitet.Tests;

public class CapacityCalculatorTests
{
    private static readonly AppSettings Settings = new()
    {
        StandardCapacity = 160, PlanningCeilingPercent = 100, MonthFactors = "1,1,1,1,1,1,0.25,1,1,1,1,1",
    };

    [Fact]
    public void Uses_standard_capacity_employment_and_month_factor()
    {
        var r = new Resource { Id = 1, EmploymentPercent = 50 };
        var cap = CapacityCalculator.ForYear(r, Settings, 2026, []);
        Assert.Equal(80, cap[0]);
        Assert.Equal(20, cap[6]);
    }

    [Fact]
    public void Override_replaces_calculated_capacity()
    {
        var r = new Resource { Id = 1 };
        var cap = CapacityCalculator.ForYear(r, Settings, 2026,
            [new CapacityOverride { ResourceId = 1, Year = 2026, Month = 3, Hours = 12 }]);
        Assert.Equal(12, cap[2]);
        Assert.Equal(160, cap[3]);
    }

    [Fact]
    public void Capacity_is_zero_outside_employment()
    {
        var r = new Resource { Id = 1, StartDate = new DateOnly(2026, 3, 15), EndDate = new DateOnly(2026, 10, 1) };
        var cap = CapacityCalculator.ForYear(r, Settings, 2026, []);
        Assert.Equal(0, cap[1]);   // feb
        Assert.Equal(160, cap[2]); // mars (börjar under månaden)
        Assert.Equal(160, cap[9]); // okt
        Assert.Equal(0, cap[10]);  // nov
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(80, 160, 50)]
    [InlineData(10, 0, 999)]
    public void Utilization(int allocated, int capacity, int expected) =>
        Assert.Equal(expected, CapacityCalculator.UtilizationPercent(allocated, capacity));
}

public class PlanningImportTests
{
    [Fact]
    public void Parses_excel_paste_with_header_and_offset_columns()
    {
        var text = "\t\tResurs\tTillhörighet\tUppdrag\tFörvaltningobjekt/Projekt\tJAN\tFEB\tMAR\tAPR\tMAJ\tJUN\tJUL\tAUG\tSEP\tOKT\tNOV\tDEC\n" +
                   "\n" +
                   "\t\tKalle Anka\tTeam 1\tUppdragX\tOmråde A\t100\t100\t100\t100\t100\t50\t\t75\t75\t75\t75\t75\n" +
                   "\t\tJoe Doe\tTeam 2\tUppdragZ\tOmråde B\t20\t20\t20\t20\t20\t20\t\t\t\t\t\t\n";
        var result = PlanningImport.Parse(text);
        Assert.Empty(result.Errors);
        Assert.Equal(2, result.Rows.Count);
        Assert.Equal("Kalle Anka", result.Rows[0].Resource);
        Assert.Equal("Område A", result.Rows[0].Object);
        Assert.Equal([100, 100, 100, 100, 100, 50, 0, 75, 75, 75, 75, 75], result.Rows[0].Hours);
        Assert.Equal(120, result.Rows[1].Hours.Sum());
    }

    [Fact]
    public void Parses_semicolon_csv_without_header()
    {
        var result = PlanningImport.Parse("Anna;Team 1;Uppdrag \"A\";Obj;1,5;2\n");
        Assert.Single(result.Rows);
        Assert.Equal(2, result.Rows[0].Hours[0]); // 1,5 avrundas till 2
    }

    [Fact]
    public void Reports_invalid_numbers()
    {
        var result = PlanningImport.Parse("Resurs;Team;Uppdrag;Objekt;JAN\nAnna;T;U;O;abc\n");
        Assert.Empty(result.Rows);
        Assert.Contains(result.Errors, e => e.Contains("abc"));
    }

    [Fact]
    public void Csv_escape_quotes_values_with_delimiters()
    {
        Assert.Equal("\"a;b\"", PlanningImport.CsvEscape("a;b"));
        Assert.Equal("plain", PlanningImport.CsvEscape("plain"));
    }
}

public class ChangeLogTests
{
    [Fact]
    public void Diff_lists_only_changed_fields()
    {
        var diff = ChangeLog.Diff(("Namn", "A", "B"), ("Team", "X", "X"), ("Aktiv", true, false));
        Assert.Equal("Namn: A → B, Aktiv: ja → nej", diff);
    }
}
