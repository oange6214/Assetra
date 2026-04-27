using Assetra.Application.Analysis;
using Xunit;

namespace Assetra.Tests.Application.Analysis;

public class DrawdownCalculatorTests
{
    [Fact]
    public void Compute_EmptySeries_ReturnsEmpty()
    {
        var calc = new DrawdownCalculator();
        Assert.Empty(calc.Compute(Array.Empty<(DateOnly, decimal)>()));
        Assert.Null(calc.ComputeMaxDrawdown(Array.Empty<(DateOnly, decimal)>()));
    }

    [Fact]
    public void ComputeMaxDrawdown_RisingThenFalling_TracksPeak()
    {
        // 100 → 120 → 90 → 110 → 80 ; peak = 120; max dd at 80 → (120-80)/120 = 0.3333
        var calc = new DrawdownCalculator();
        var values = new List<(DateOnly, decimal)>
        {
            (new DateOnly(2026, 1, 1), 100m),
            (new DateOnly(2026, 1, 2), 120m),
            (new DateOnly(2026, 1, 3), 90m),
            (new DateOnly(2026, 1, 4), 110m),
            (new DateOnly(2026, 1, 5), 80m),
        };
        var mdd = calc.ComputeMaxDrawdown(values);
        Assert.NotNull(mdd);
        Assert.InRange((double)mdd!.Value, 0.3333 - 0.001, 0.3333 + 0.001);
    }

    [Fact]
    public void Compute_MonotonicallyIncreasing_ZeroDrawdown()
    {
        var calc = new DrawdownCalculator();
        var values = new List<(DateOnly, decimal)>
        {
            (new DateOnly(2026, 1, 1), 100m),
            (new DateOnly(2026, 1, 2), 110m),
            (new DateOnly(2026, 1, 3), 130m),
        };
        Assert.Equal(0m, calc.ComputeMaxDrawdown(values)!.Value);
    }
}
