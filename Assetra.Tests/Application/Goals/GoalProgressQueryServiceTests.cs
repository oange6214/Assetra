using Assetra.Application.Goals;
using Assetra.Core.Models;
using Xunit;

namespace Assetra.Tests.Application.Goals;

public class GoalProgressQueryServiceTests
{
    private static readonly Guid GoalId = Guid.NewGuid();

    private static FinancialGoal Goal(decimal target = 100_000m, decimal current = 0m) =>
        new(GoalId, "Test", target, current, null, null);

    private static GoalFundingRule Rule(
        decimal amount,
        RecurrenceFrequency freq,
        DateOnly start,
        DateOnly? end = null,
        bool enabled = true,
        Guid? goalId = null) =>
        new(Guid.NewGuid(), goalId ?? GoalId, amount, freq, null, start, end, enabled);

    [Fact]
    public void Compute_NoRules_AccumulatedZero()
    {
        var svc = new GoalProgressQueryService();
        var result = svc.Compute(Goal(), [], currentAmount: 0m, asOf: new DateOnly(2026, 4, 28));
        Assert.Equal(0m, result.AccumulatedFunding);
        Assert.Equal(0m, result.ProgressPercent);
        Assert.False(result.IsAchieved);
    }

    [Fact]
    public void Compute_MonthlyRule_AccumulatesPerMonth()
    {
        var svc = new GoalProgressQueryService();
        var rule = Rule(5_000m, RecurrenceFrequency.Monthly, new DateOnly(2026, 1, 1));
        var result = svc.Compute(Goal(), [rule], currentAmount: null, asOf: new DateOnly(2026, 4, 1));
        // Jan, Feb, Mar, Apr → 4 occurrences × 5000
        Assert.Equal(20_000m, result.AccumulatedFunding);
    }

    [Fact]
    public void Compute_DailyRule_AccumulatesPerDay()
    {
        var svc = new GoalProgressQueryService();
        var rule = Rule(100m, RecurrenceFrequency.Daily, new DateOnly(2026, 4, 1));
        var result = svc.Compute(Goal(), [rule], currentAmount: null, asOf: new DateOnly(2026, 4, 5));
        Assert.Equal(500m, result.AccumulatedFunding);
    }

    [Fact]
    public void Compute_DisabledRule_Ignored()
    {
        var svc = new GoalProgressQueryService();
        var rule = Rule(5_000m, RecurrenceFrequency.Monthly, new DateOnly(2026, 1, 1), enabled: false);
        var result = svc.Compute(Goal(), [rule], currentAmount: null, asOf: new DateOnly(2026, 4, 1));
        Assert.Equal(0m, result.AccumulatedFunding);
    }

    [Fact]
    public void Compute_OtherGoalRule_Ignored()
    {
        var svc = new GoalProgressQueryService();
        var rule = Rule(5_000m, RecurrenceFrequency.Monthly, new DateOnly(2026, 1, 1), goalId: Guid.NewGuid());
        var result = svc.Compute(Goal(), [rule], currentAmount: null, asOf: new DateOnly(2026, 4, 1));
        Assert.Equal(0m, result.AccumulatedFunding);
    }

    [Fact]
    public void Compute_EndDateInPast_StopsAccumulation()
    {
        var svc = new GoalProgressQueryService();
        var rule = Rule(5_000m, RecurrenceFrequency.Monthly,
            new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 1));
        var result = svc.Compute(Goal(), [rule], currentAmount: null, asOf: new DateOnly(2026, 6, 1));
        // Jan, Feb → 2 × 5000
        Assert.Equal(10_000m, result.AccumulatedFunding);
    }

    [Fact]
    public void Compute_StartDateAfterAsOf_NotCounted()
    {
        var svc = new GoalProgressQueryService();
        var rule = Rule(5_000m, RecurrenceFrequency.Monthly, new DateOnly(2026, 5, 1));
        var result = svc.Compute(Goal(), [rule], currentAmount: null, asOf: new DateOnly(2026, 4, 1));
        Assert.Equal(0m, result.AccumulatedFunding);
    }

    [Fact]
    public void Compute_CurrentAmount_OverridesAccumulatedForProgress()
    {
        var svc = new GoalProgressQueryService();
        var rule = Rule(5_000m, RecurrenceFrequency.Monthly, new DateOnly(2026, 1, 1));
        var result = svc.Compute(Goal(target: 100_000m), [rule],
            currentAmount: 50_000m, asOf: new DateOnly(2026, 4, 1));
        Assert.Equal(20_000m, result.AccumulatedFunding);
        Assert.Equal(50_000m, result.CurrentAmount);
        Assert.Equal(50m, result.ProgressPercent);
    }

    [Fact]
    public void Compute_CurrentReachesTarget_IsAchievedTrue()
    {
        var svc = new GoalProgressQueryService();
        var result = svc.Compute(Goal(target: 100_000m), [],
            currentAmount: 100_000m, asOf: new DateOnly(2026, 4, 1));
        Assert.True(result.IsAchieved);
        Assert.Equal(100m, result.ProgressPercent);
    }

    [Fact]
    public void Compute_ProgressCappedAt100Percent()
    {
        var svc = new GoalProgressQueryService();
        var result = svc.Compute(Goal(target: 100_000m), [],
            currentAmount: 250_000m, asOf: new DateOnly(2026, 4, 1));
        Assert.Equal(100m, result.ProgressPercent);
    }

    [Fact]
    public void Compute_NegativeAmount_Ignored()
    {
        var svc = new GoalProgressQueryService();
        var rule = Rule(-5_000m, RecurrenceFrequency.Monthly, new DateOnly(2026, 1, 1));
        var result = svc.Compute(Goal(), [rule], currentAmount: null, asOf: new DateOnly(2026, 4, 1));
        Assert.Equal(0m, result.AccumulatedFunding);
    }

    [Fact]
    public void Compute_QuarterlyFrequency_AccumulatesEveryThreeMonths()
    {
        var svc = new GoalProgressQueryService();
        var rule = Rule(15_000m, RecurrenceFrequency.Quarterly, new DateOnly(2026, 1, 1));
        var result = svc.Compute(Goal(), [rule], currentAmount: null, asOf: new DateOnly(2026, 12, 31));
        // Q1 (Jan), Q2 (Apr), Q3 (Jul), Q4 (Oct) → 4 × 15000
        Assert.Equal(60_000m, result.AccumulatedFunding);
    }

    [Fact]
    public void Compute_YearlyFrequency_AccumulatesPerYear()
    {
        var svc = new GoalProgressQueryService();
        var rule = Rule(50_000m, RecurrenceFrequency.Yearly, new DateOnly(2024, 5, 1));
        var result = svc.Compute(Goal(), [rule], currentAmount: null, asOf: new DateOnly(2026, 5, 1));
        // 2024, 2025, 2026 → 3 × 50000
        Assert.Equal(150_000m, result.AccumulatedFunding);
    }

    [Fact]
    public void Compute_BiWeeklyFrequency_AccumulatesEveryFourteenDays()
    {
        var svc = new GoalProgressQueryService();
        var rule = Rule(1_000m, RecurrenceFrequency.BiWeekly, new DateOnly(2026, 1, 1));
        var result = svc.Compute(Goal(), [rule], currentAmount: null, asOf: new DateOnly(2026, 1, 28));
        // Day 0 (1/1), 14 (1/15), 28 (1/29 — but until 1/28 → only 0, 14)
        // 28 - 0 = 28, 28/14 + 1 = 3 → 1/1, 1/15, 1/29 — 1/29 > 1/28 so should be 2.
        // Our formula returns 28/14+1 = 3 which is wrong by 1. Let me check.
        // Actually start=1/1 (day 0), until=1/28 (day 27). 27/14=1, +1=2. Correct.
        Assert.Equal(2_000m, result.AccumulatedFunding);
    }
}
