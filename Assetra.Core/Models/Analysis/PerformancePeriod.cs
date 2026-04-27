namespace Assetra.Core.Models.Analysis;

public sealed record PerformancePeriod(DateOnly Start, DateOnly End)
{
    public static PerformancePeriod Month(int year, int month)
    {
        var start = new DateOnly(year, month, 1);
        var end = start.AddMonths(1).AddDays(-1);
        return new PerformancePeriod(start, end);
    }

    public static PerformancePeriod Year(int year) =>
        new(new DateOnly(year, 1, 1), new DateOnly(year, 12, 31));
}
