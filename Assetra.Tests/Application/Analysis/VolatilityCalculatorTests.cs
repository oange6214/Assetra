using Assetra.Application.Analysis;
using Xunit;

namespace Assetra.Tests.Application.Analysis;

public class VolatilityCalculatorTests
{
    [Fact]
    public void ComputeAnnualized_ReturnsNull_WhenLessThanTwoPoints()
    {
        var calc = new VolatilityCalculator();
        var values = new List<(DateOnly, decimal)>
        {
            (new DateOnly(2026, 1, 1), 100m),
        };
        Assert.Null(calc.ComputeAnnualized(values));
    }

    [Fact]
    public void ComputeAnnualized_FlatSeries_ReturnsZero()
    {
        var calc = new VolatilityCalculator();
        var values = new List<(DateOnly, decimal)>
        {
            (new DateOnly(2026, 1, 1), 100m),
            (new DateOnly(2026, 1, 2), 100m),
            (new DateOnly(2026, 1, 3), 100m),
        };
        var result = calc.ComputeAnnualized(values);
        Assert.NotNull(result);
        Assert.Equal(0m, result!.Value, 6);
    }

    [Fact]
    public void ComputeAnnualized_KnownSeries_ProducesPositiveVolatility()
    {
        // Returns: +10%, -5%, +5%
        var calc = new VolatilityCalculator();
        var values = new List<(DateOnly, decimal)>
        {
            (new DateOnly(2026, 1, 1), 100m),
            (new DateOnly(2026, 1, 2), 110m),
            (new DateOnly(2026, 1, 3), 104.5m),
            (new DateOnly(2026, 1, 4), 109.725m),
        };
        var result = calc.ComputeAnnualized(values);
        Assert.NotNull(result);
        Assert.True(result!.Value > 0m);
        // Daily std ~ 0.0764, annualized ~ 0.0764 × sqrt(252) ~ 1.21
        Assert.InRange((double)result.Value, 1.0, 1.5);
    }
}
