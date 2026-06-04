using Assetra.Core.Models;
using Assetra.WPF.Features.Goals;
using Xunit;

namespace Assetra.Tests.WPF;

public sealed class GoalRowViewModelTests
{
    [Fact]
    public void TrackingSourceLabel_ShowsPortfolioGroup_WhenGoalLinksGroup()
    {
        var goal = new FinancialGoal(
            Guid.NewGuid(),
            "House fund",
            1_000_000m,
            100_000m,
            null,
            null,
            LinkedAssetClass: null,
            PortfolioGroupId: Guid.NewGuid());

        var row = new GoalRowViewModel(goal);

        Assert.Equal("portfolioGroup", row.TrackingSourceKind);
        Assert.Equal("Portfolio group", row.TrackingSourceLabel);
    }

    [Fact]
    public void TrackingSourceLabel_ShowsAssetClass_WhenGoalLinksAssetClass()
    {
        var goal = new FinancialGoal(
            Guid.NewGuid(),
            "Investment goal",
            1_000_000m,
            100_000m,
            null,
            null,
            LinkedAssetClass: "Investments");

        var row = new GoalRowViewModel(goal);

        Assert.Equal("assetClass", row.TrackingSourceKind);
        Assert.Equal("Auto-tracked: Investments", row.TrackingSourceLabel);
    }

    [Fact]
    public void TrackingSourceLabel_ShowsFire_WhenGoalComesFromFire()
    {
        var goal = new FinancialGoal(
            Guid.NewGuid(),
            "FIRE",
            15_000_000m,
            8_500_000m,
            null,
            "Generated from FIRE scenario \"Base\" (scenario-id).");

        var row = new GoalRowViewModel(goal);

        Assert.Equal("fire", row.TrackingSourceKind);
        Assert.Equal("FIRE sync", row.TrackingSourceLabel);
    }
}
