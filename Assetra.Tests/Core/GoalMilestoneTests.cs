using Assetra.Core.Models;
using Xunit;

namespace Assetra.Tests.Core;

public class GoalMilestoneTests
{
    [Fact]
    public void GoalMilestone_RecordEquality_TreatsValueEqualInstancesAsEqual()
    {
        var goalId = Guid.NewGuid();
        var date = new DateOnly(2030, 1, 1);
        var a = new GoalMilestone(Guid.Empty, goalId, date, 1_000m, "First", false);
        var b = new GoalMilestone(Guid.Empty, goalId, date, 1_000m, "First", false);

        Assert.Equal(a, b);
    }

    [Fact]
    public void GoalMilestone_WithExpression_ProducesNewInstanceWithChange()
    {
        var original = new GoalMilestone(Guid.NewGuid(), Guid.NewGuid(),
            new DateOnly(2030, 1, 1), 1_000m, "First", false);
        var achieved = original with { IsAchieved = true };

        Assert.False(original.IsAchieved);
        Assert.True(achieved.IsAchieved);
        Assert.Equal(original.Id, achieved.Id);
    }
}
