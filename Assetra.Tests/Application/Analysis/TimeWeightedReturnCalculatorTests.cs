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
}
