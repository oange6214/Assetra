using Assetra.Application.Analysis;
using Assetra.Core.Models.Analysis;
using Xunit;

namespace Assetra.Tests.Application.Analysis;

public class TimeWeightedReturnCalculatorTests
{
    [Fact]
    public void Compute_NoFlows_ReturnsSimpleReturn()
    {
        var calc = new TimeWeightedReturnCalculator();
        var valuations = new[]
        {
            (new DateOnly(2026, 1, 1), 100m),
            (new DateOnly(2026, 12, 31), 110m),
        };
        var r = calc.Compute(valuations, Array.Empty<CashFlow>());
        Assert.NotNull(r);
        Assert.Equal(0.10m, r.Value);
    }

    [Fact]
    public void Compute_FlowMidPeriod_IsolatesItFromReturn()
    {
        // Start 100; mid +50 (deposit); end 165 → segments: 100→100 (0%), 100→(165-50)=115 (15%) → TWR=15%
        var calc = new TimeWeightedReturnCalculator();
        var valuations = new[]
        {
            (new DateOnly(2026, 1, 1), 100m),
            (new DateOnly(2026, 6, 30), 100m),
            (new DateOnly(2026, 12, 31), 165m),
        };
        var flows = new[] { new CashFlow(new DateOnly(2026, 12, 31), 50m) };
        var r = calc.Compute(valuations, flows);
        Assert.NotNull(r);
        Assert.InRange((double)r!.Value, 0.149, 0.151);
    }

    [Fact]
    public void Compute_LossScenario_ReturnsNegative()
    {
        var calc = new TimeWeightedReturnCalculator();
        var valuations = new[]
        {
            (new DateOnly(2026, 1, 1), 100m),
            (new DateOnly(2026, 12, 31), 80m),
        };
        var r = calc.Compute(valuations, Array.Empty<CashFlow>());
        Assert.NotNull(r);
        Assert.Equal(-0.20m, r.Value);
    }

    [Fact]
    public void ComputeSeries_FirstPointIsZero_EndpointMatchesCompute()
    {
        var calc = new TimeWeightedReturnCalculator();
        var valuations = new[]
        {
            (new DateOnly(2026, 1, 1), 100m),
            (new DateOnly(2026, 1, 2), 110m),
            (new DateOnly(2026, 1, 3), 121m),
        };
        var flows = Array.Empty<CashFlow>();

        var series = calc.ComputeSeries(valuations, flows);

        Assert.NotNull(series);
        Assert.Equal(3, series!.Count);
        Assert.Equal(0m, series[0].CumulativeTwr);                     // 首點 = 0%
        Assert.Equal(calc.Compute(valuations, flows), series[^1].CumulativeTwr); // 末點 = Compute
    }

    [Fact]
    public void ComputeSeries_FlowOnSellDay_DividesOut()
    {
        // day2: 100→200 = +100%；day3: 200→110 但當天 flow −90（投組角度賣出）→ segReturn 0
        var calc = new TimeWeightedReturnCalculator();
        var valuations = new[]
        {
            (new DateOnly(2026, 1, 1), 100m),
            (new DateOnly(2026, 1, 2), 200m),
            (new DateOnly(2026, 1, 3), 110m),
        };
        var flows = new[] { new CashFlow(new DateOnly(2026, 1, 3), -90m) };

        var series = calc.ComputeSeries(valuations, flows);

        Assert.NotNull(series);
        Assert.Equal(1.0m, series![^1].CumulativeTwr); // 仍 +100%，賣出不計入報酬
    }

    [Fact]
    public void ComputeSeries_BelowTwoPoints_ReturnsNull()
    {
        var calc = new TimeWeightedReturnCalculator();
        var series = calc.ComputeSeries(
            new[] { (new DateOnly(2026, 1, 1), 100m) },
            Array.Empty<CashFlow>());
        Assert.Null(series);
    }
}
