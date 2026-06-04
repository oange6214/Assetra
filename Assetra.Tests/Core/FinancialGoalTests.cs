using Assetra.Core.Models;
using Xunit;

namespace Assetra.Tests.Core;

public sealed class FinancialGoalTests
{
    [Fact]
    public void IsAutoTracked_ReturnsTrue_WhenPortfolioGroupIsLinked()
    {
        var goal = new FinancialGoal(
            Guid.NewGuid(),
            "Retirement bucket",
            1_000_000m,
            0m,
            null,
            null,
            LinkedAssetClass: null,
            PortfolioGroupId: Guid.NewGuid());

        Assert.True(goal.IsAutoTracked);
    }
}
