using Assetra.Application.Goals;
using Xunit;

namespace Assetra.Tests.Application.Goals;

public class GoalPlanningServiceTests
{
    [Fact]
    public void RequiredMonthlyContribution_ZeroReturn_DividesShortfallEvenly()
    {
        var pmt = GoalPlanningService.RequiredMonthlyContribution(
            currentAmount: 0m, targetAmount: 12_000m, annualReturnRate: 0m, months: 12);

        Assert.Equal(1_000m, pmt);
    }

    [Fact]
    public void RequiredMonthlyContribution_PositiveReturn_AccountsForCompounding()
    {
        // 5% annual, 12 months, target 12_000, no PV → PMT should be < 1000 (compounding helps)
        var pmt = GoalPlanningService.RequiredMonthlyContribution(
            currentAmount: 0m, targetAmount: 12_000m, annualReturnRate: 0.05m, months: 12);

        Assert.NotNull(pmt);
        Assert.True(pmt < 1_000m);
        Assert.True(pmt > 950m);
    }

    [Fact]
    public void RequiredMonthlyContribution_PvAlreadyExceedsTargetAfterGrowth_ReturnsZero()
    {
        // 100k @ 10% over 36 months grows to ~134k > 120k target
        var pmt = GoalPlanningService.RequiredMonthlyContribution(
            currentAmount: 100_000m, targetAmount: 120_000m, annualReturnRate: 0.10m, months: 36);

        Assert.Equal(0m, pmt);
    }

    [Fact]
    public void RequiredMonthlyContribution_DeadlinePassedAndShortfall_ReturnsNull()
    {
        var pmt = GoalPlanningService.RequiredMonthlyContribution(
            currentAmount: 5_000m, targetAmount: 10_000m, annualReturnRate: 0.05m, months: 0);

        Assert.Null(pmt);
    }

    [Fact]
    public void RequiredMonthlyContribution_DeadlinePassedAndAlreadyMet_ReturnsZero()
    {
        var pmt = GoalPlanningService.RequiredMonthlyContribution(
            currentAmount: 10_000m, targetAmount: 10_000m, annualReturnRate: 0.05m, months: 0);

        Assert.Equal(0m, pmt);
    }

    [Fact]
    public void RequiredMonthlyContribution_BeginningOfPeriod_RequiresLessThanEndOfPeriod()
    {
        var endOfPeriod = GoalPlanningService.RequiredMonthlyContribution(
            0m, 12_000m, 0.05m, 12, contributionAtBeginningOfPeriod: false)!.Value;
        var beginningOfPeriod = GoalPlanningService.RequiredMonthlyContribution(
            0m, 12_000m, 0.05m, 12, contributionAtBeginningOfPeriod: true)!.Value;

        Assert.True(beginningOfPeriod < endOfPeriod);
    }

    [Fact]
    public void RequiredMonthlyContribution_NegativeRate_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            GoalPlanningService.RequiredMonthlyContribution(0m, 1_000m, -0.01m, 12));
    }

    [Fact]
    public void MonthsToReachTarget_AlreadyMet_ReturnsZero()
    {
        var months = GoalPlanningService.MonthsToReachTarget(
            currentAmount: 100m, targetAmount: 50m, annualReturnRate: 0m, monthlyContribution: 0m);

        Assert.Equal(0, months);
    }

    [Fact]
    public void MonthsToReachTarget_ZeroReturnZeroPmt_ReturnsNull()
    {
        var months = GoalPlanningService.MonthsToReachTarget(
            currentAmount: 0m, targetAmount: 1_000m, annualReturnRate: 0m, monthlyContribution: 0m);

        Assert.Null(months);
    }

    [Fact]
    public void MonthsToReachTarget_ZeroReturnPositivePmt_DividesEvenly()
    {
        var months = GoalPlanningService.MonthsToReachTarget(
            currentAmount: 0m, targetAmount: 1_200m, annualReturnRate: 0m, monthlyContribution: 100m);

        Assert.Equal(12, months);
    }

    [Fact]
    public void MonthsToReachTarget_PositiveReturn_FasterThanZeroReturn()
    {
        var withReturn = GoalPlanningService.MonthsToReachTarget(0m, 12_000m, 0.10m, 1_000m);
        var withoutReturn = GoalPlanningService.MonthsToReachTarget(0m, 12_000m, 0m, 1_000m);

        Assert.NotNull(withReturn);
        Assert.NotNull(withoutReturn);
        Assert.True(withReturn <= withoutReturn);
    }

    [Fact]
    public void MonthsToReachTarget_RoundTripWithRequiredContribution()
    {
        // If we ask for the PMT to reach 12000 in 12 months, then plug that PMT back in,
        // we should get 12 months (or very close — within 1 month due to rounding).
        var pmt = GoalPlanningService.RequiredMonthlyContribution(0m, 12_000m, 0.05m, 12)!.Value;
        var months = GoalPlanningService.MonthsToReachTarget(0m, 12_000m, 0.05m, pmt);

        Assert.NotNull(months);
        Assert.InRange(months!.Value, 11, 13);
    }
}
