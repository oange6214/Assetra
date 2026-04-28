using Assetra.Core.Models;

namespace Assetra.Application.Goals;

/// <summary>
/// 純函式 service：依 funding rule 歷史與當前資產淨值計算 <see cref="GoalProgress"/>。
/// 「累計撥款」= 對每條 rule 計算 startDate..min(endDate, asOf) 之間的撥款次數 × amount。
/// 「進度百分比」優先以 <c>currentAmount</c>（即 BalanceSheet 對應之資產淨值）為準；caller 若無法
/// 提供（傳入 null），則退回以 <c>AccumulatedFunding</c> 為近似值（過於樂觀，僅供啟動期參考）。
/// </summary>
public sealed class GoalProgressQueryService
{
    public GoalProgress Compute(
        FinancialGoal goal,
        IReadOnlyList<GoalFundingRule> fundingRules,
        decimal? currentAmount,
        DateOnly asOf)
    {
        ArgumentNullException.ThrowIfNull(goal);
        ArgumentNullException.ThrowIfNull(fundingRules);

        var accumulated = 0m;
        foreach (var rule in fundingRules)
        {
            if (!rule.IsEnabled) continue;
            if (rule.GoalId != goal.Id) continue;
            if (rule.Amount <= 0m) continue;
            if (rule.StartDate > asOf) continue;
            var until = rule.EndDate is { } ed && ed < asOf ? ed : asOf;
            var occurrences = CountOccurrences(rule.StartDate, until, rule.Frequency);
            accumulated += rule.Amount * occurrences;
        }

        var actualCurrent = currentAmount ?? accumulated;
        var progress = goal.TargetAmount > 0m
            ? Math.Min(actualCurrent / goal.TargetAmount * 100m, 100m)
            : 0m;
        var achieved = goal.TargetAmount > 0m && actualCurrent >= goal.TargetAmount;

        return new GoalProgress(goal.Id, goal.TargetAmount, accumulated, actualCurrent, progress, achieved);
    }

    internal static int CountOccurrences(DateOnly start, DateOnly until, RecurrenceFrequency frequency)
    {
        if (until < start) return 0;
        return frequency switch
        {
            RecurrenceFrequency.Daily => until.DayNumber - start.DayNumber + 1,
            RecurrenceFrequency.Weekly => (until.DayNumber - start.DayNumber) / 7 + 1,
            RecurrenceFrequency.BiWeekly => (until.DayNumber - start.DayNumber) / 14 + 1,
            RecurrenceFrequency.Monthly => MonthsBetweenInclusive(start, until),
            RecurrenceFrequency.Quarterly => MonthsBetweenInclusive(start, until, step: 3),
            RecurrenceFrequency.Yearly => YearsBetweenInclusive(start, until),
            _ => 0,
        };
    }

    private static int MonthsBetweenInclusive(DateOnly start, DateOnly until, int step = 1)
    {
        var months = (until.Year - start.Year) * 12 + (until.Month - start.Month);
        if (until.Day < start.Day) months--;
        if (months < 0) return 0;
        return months / step + 1;
    }

    private static int YearsBetweenInclusive(DateOnly start, DateOnly until)
    {
        var years = until.Year - start.Year;
        if (until.Month < start.Month || (until.Month == start.Month && until.Day < start.Day))
            years--;
        if (years < 0) return 0;
        return years + 1;
    }
}
