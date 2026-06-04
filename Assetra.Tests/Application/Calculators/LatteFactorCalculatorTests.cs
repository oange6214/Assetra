using Assetra.Application.Calculators;
using Assetra.Core.Models.Calculators;
using Xunit;
namespace Assetra.Tests.Application.Calculators;
public class LatteFactorCalculatorTests
{
    [Fact] // WHY: 零報酬時複利後總值=純投入；多賺=0
    public void ZeroReturn_FvEqualsContributed()
    {
        var r = new LatteFactorCalculator().Calculate(new(100m, LatteFrequency.Monthly, 0m, 10));
        Assert.Equal(12_000m, r.TotalContributed);   // 100×120
        Assert.Equal(12_000m, r.FutureValue);
        Assert.Equal(0m, r.Gain);
    }
    [Fact] // WHY: 有報酬時 FV 須大於投入，且每日換算 ≈ ×365/12
    public void DailyWithReturn_GrowsAboveContributed()
    {
        var r = new LatteFactorCalculator().Calculate(new(50m, LatteFrequency.Daily, 0.06m, 20));
        Assert.True(r.FutureValue > r.TotalContributed);
        Assert.Equal(decimal.Round(50m*365m/12m*240m,0), r.TotalContributed);
    }
}
