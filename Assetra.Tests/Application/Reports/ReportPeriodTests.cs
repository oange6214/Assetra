using Assetra.Core.Models.Reports;
using Xunit;

namespace Assetra.Tests.Application.Reports;

public class ReportPeriodTests
{
    [Fact]
    public void Month_ProducesFullCalendarMonth()
    {
        var p = ReportPeriod.Month(2026, 4);
        Assert.Equal(new DateOnly(2026, 4, 1), p.Start);
        Assert.Equal(new DateOnly(2026, 4, 30), p.End);
    }

    [Fact]
    public void Year_ProducesFullCalendarYear()
    {
        var p = ReportPeriod.Year(2026);
        Assert.Equal(new DateOnly(2026, 1, 1), p.Start);
        Assert.Equal(new DateOnly(2026, 12, 31), p.End);
    }

    [Fact]
    public void Prior_ReturnsEqualLengthPriorPeriod()
    {
        var p = ReportPeriod.Month(2026, 4); // 30 days
        var prior = p.Prior();
        Assert.Equal(new DateOnly(2026, 3, 31), prior.End); // ends day before April
        var len = prior.End.DayNumber - prior.Start.DayNumber + 1;
        Assert.Equal(30, len);
    }

    [Fact]
    public void Contains_DateInsideAndOutside()
    {
        var p = ReportPeriod.Month(2026, 4);
        Assert.True(p.Contains(new DateOnly(2026, 4, 15)));
        Assert.False(p.Contains(new DateOnly(2026, 5, 1)));
    }
}
