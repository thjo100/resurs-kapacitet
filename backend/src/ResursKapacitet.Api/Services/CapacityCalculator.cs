using ResursKapacitet.Api.Data;

namespace ResursKapacitet.Api.Services;

public static class CapacityCalculator
{
    /// <summary>
    /// Planeringsbar kapacitet per månad (JAN..DEC) för en resurs ett visst år.
    /// Kapacitet = (egen kapacitet ?? standard) × sysselsättningsgrad × månadsfaktor × planeringstak.
    /// En kapacitetsavvikelse ersätter det beräknade värdet. Utanför anställningsperioden är kapaciteten 0.
    /// </summary>
    public static int[] ForYear(Resource resource, AppSettings settings, int year,
        IEnumerable<CapacityOverride>? overrides = null)
    {
        var factors = settings.GetMonthFactors();
        var baseCapacity = (resource.CapacityPerMonth ?? settings.StandardCapacity)
                           * (resource.EmploymentPercent / 100.0)
                           * (settings.PlanningCeilingPercent / 100.0);
        var result = new int[12];
        var ovr = (overrides ?? resource.CapacityOverrides)
            .Where(o => o.ResourceId == resource.Id && o.Year == year)
            .ToDictionary(o => o.Month, o => o.Hours);

        for (var m = 1; m <= 12; m++)
        {
            if (!IsEmployed(resource, year, m))
            {
                result[m - 1] = 0;
                continue;
            }

            result[m - 1] = ovr.TryGetValue(m, out var h)
                ? h
                : (int)Math.Round(baseCapacity * factors[m - 1], MidpointRounding.AwayFromZero);
        }

        return result;
    }

    public static bool IsEmployed(Resource resource, int year, int month)
    {
        var first = new DateOnly(year, month, 1);
        var last = first.AddMonths(1).AddDays(-1);
        if (resource.StartDate is { } start && start > last) return false;
        if (resource.EndDate is { } end && end < first) return false;
        return true;
    }

    public static int UtilizationPercent(int allocated, int capacity) =>
        capacity <= 0 ? (allocated > 0 ? 999 : 0) : (int)Math.Round(allocated * 100.0 / capacity);
}
