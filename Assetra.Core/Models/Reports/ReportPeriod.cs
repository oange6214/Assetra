namespace Assetra.Core.Models.Reports;

/// <summary>
/// 報表期間（含起訖日，皆 inclusive）。
/// </summary>
public sealed record ReportPeriod(DateOnly Start, DateOnly End)
{
    public static ReportPeriod Month(int year, int month)
    {
        var start = new DateOnly(year, month, 1);
        var end = start.AddMonths(1).AddDays(-1);
        return new ReportPeriod(start, end);
    }

    public static ReportPeriod Year(int year)
        => new(new DateOnly(year, 1, 1), new DateOnly(year, 12, 31));

    /// <summary>等長度的上一期（用於 QoQ / MoM 比較）。</summary>
    public ReportPeriod Prior()
    {
        var lengthDays = End.DayNumber - Start.DayNumber + 1;
        var priorEnd = Start.AddDays(-1);
        var priorStart = priorEnd.AddDays(-(lengthDays - 1));
        return new ReportPeriod(priorStart, priorEnd);
    }

    public bool Contains(DateOnly date) => date >= Start && date <= End;
    public bool Contains(DateTime dt) => Contains(DateOnly.FromDateTime(dt));
}
