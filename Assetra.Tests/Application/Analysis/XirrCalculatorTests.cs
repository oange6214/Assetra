using Assetra.Application.Analysis;
using Assetra.Core.Models.Analysis;
using Xunit;

namespace Assetra.Tests.Application.Analysis;

public class XirrCalculatorTests
{
    [Fact]
    public void Compute_ClassicTextbookCase_ConvergesToKnownRate()
    {
        // -10000 on day 0, +10750 after 1 year ≈ 7.5%
        var calc = new XirrCalculator();
        var flows = new List<CashFlow>
        {
            new(new DateOnly(2025, 1, 1), -10000m),
            new(new DateOnly(2026, 1, 1), 10750m),
        };
        var r = calc.Compute(flows);
        Assert.NotNull(r);
        Assert.InRange((double)r!.Value, 0.074, 0.076);
    }

    [Fact]
    public void Compute_MultipleFlowsOverYear_ReturnsPositiveRate()
    {
        // Excel XIRR sample: -1000 then small inflows totaling > 1000
        var calc = new XirrCalculator();
        var flows = new List<CashFlow>
        {
            new(new DateOnly(2025, 1, 1), -1000m),
            new(new DateOnly(2025, 4, 1), 100m),
            new(new DateOnly(2025, 7, 1), 200m),
            new(new DateOnly(2026, 1, 1), 800m),
        };
        var r = calc.Compute(flows);
        Assert.NotNull(r);
        Assert.True(r.Value > 0m, "expected positive XIRR");
    }

    [Fact]
    public void Compute_AllPositive_ReturnsNull()
    {
        var calc = new XirrCalculator();
        var flows = new List<CashFlow>
        {
            new(new DateOnly(2025, 1, 1), 100m),
            new(new DateOnly(2025, 6, 1), 200m),
        };
        Assert.Null(calc.Compute(flows));
    }

    [Fact]
    public void Compute_TooFewFlows_ReturnsNull()
    {
        var calc = new XirrCalculator();
        Assert.Null(calc.Compute(new[] { new CashFlow(new DateOnly(2025, 1, 1), -100m) }));
    }

    [Fact]
    public void Compute_LossScenario_ReturnsNegativeRate()
    {
        var calc = new XirrCalculator();
        var flows = new List<CashFlow>
        {
            new(new DateOnly(2025, 1, 1), -10000m),
            new(new DateOnly(2026, 1, 1), 9000m),
        };
        var r = calc.Compute(flows);
        Assert.NotNull(r);
        Assert.True(r.Value < 0m);
    }
}
