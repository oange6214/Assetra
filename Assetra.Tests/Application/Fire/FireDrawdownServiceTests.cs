using Assetra.Application.Fire;
using Assetra.Core.Models.Fire;
using Xunit;

namespace Assetra.Tests.Application.Fire;

public sealed class FireDrawdownServiceTests
{
    [Fact]
    public void ProjectDrawdown_WhenBalanceLastsToLifeExpectancy_DoesNotWarn()
    {
        var service = new FireDrawdownService();

        var result = service.ProjectDrawdown(
            startingBalance: 20_000_000m,
            annualRetirementExpenses: 600_000m,
            expectedAnnualReturn: 0.04m,
            currentAge: 45,
            lifeExpectancyAge: 90);

        Assert.DoesNotContain(
            result.Warnings,
            warning => warning.Code == FireProjectionWarningCode.DrawdownDepletesBeforeLifeExpectancy);
        Assert.Equal(46, result.DrawdownPath.Count);
        Assert.Equal(45, result.DrawdownPath[0].Age);
    }

    [Fact]
    public void ProjectDrawdown_WhenBalanceRunsOutBeforeLifeExpectancy_ReturnsWarning()
    {
        var service = new FireDrawdownService();

        var result = service.ProjectDrawdown(
            startingBalance: 1_000_000m,
            annualRetirementExpenses: 600_000m,
            expectedAnnualReturn: 0m,
            currentAge: 45,
            lifeExpectancyAge: 90);

        Assert.Contains(
            result.Warnings,
            warning => warning.Code == FireProjectionWarningCode.DrawdownDepletesBeforeLifeExpectancy);
        Assert.Contains(result.DrawdownPath, point => point.EndingBalance <= 0m);
    }
}
