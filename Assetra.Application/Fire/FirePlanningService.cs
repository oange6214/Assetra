using Assetra.Core.Interfaces.Fire;
using Assetra.Core.Models.Fire;

namespace Assetra.Application.Fire;

public sealed class FirePlanningService : IFirePlanningService
{
    public FirePlanningProjection Project(
        FireScenario scenario,
        IReadOnlyList<FireCashFlowEvent> cashFlowEvents,
        int currentYear,
        int maxYears = 80)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(cashFlowEvents);

        if (maxYears <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxYears), "Max years must be positive.");
        if (scenario.WithdrawalRate <= 0m)
            throw new ArgumentOutOfRangeException(nameof(scenario.WithdrawalRate), "Withdrawal rate must be positive.");
        if (scenario.WithdrawalRate > 1m)
            throw new ArgumentOutOfRangeException(nameof(scenario.WithdrawalRate), "Withdrawal rate must be less than or equal to 100%.");
        if (scenario.AnnualExpenses <= 0m)
            throw new ArgumentOutOfRangeException(nameof(scenario.AnnualExpenses), "Annual expenses must be positive.");
        if (scenario.AnnualSavings < 0m)
            throw new ArgumentOutOfRangeException(nameof(scenario.AnnualSavings), "Annual savings cannot be negative.");
        if (scenario.ExpectedAnnualReturn <= -1m)
            throw new ArgumentOutOfRangeException(nameof(scenario.ExpectedAnnualReturn), "Expected annual return must be greater than -100%.");

        var warnings = new List<FireProjectionWarning>();
        if (scenario.ReturnMode == FireReturnMode.Nominal && scenario.InflationRate is null)
        {
            warnings.Add(new FireProjectionWarning(
                FireProjectionWarningCode.InflationMissingForNominalMode,
                "名目報酬模式需要通膨率，否則會高估退休後購買力。"));
        }

        var requiredAssets = scenario.CustomTargetAmount
            ?? (scenario.RetirementAnnualExpenses ?? scenario.AnnualExpenses) / scenario.WithdrawalRate;

        var currentNetWorth = scenario.CurrentNetWorthOverride ?? 0m;
        if (currentNetWorth < 0m)
            throw new ArgumentOutOfRangeException(nameof(scenario.CurrentNetWorthOverride), "Current net worth cannot be negative.");

        var balance = currentNetWorth;
        var path = new List<FireWealthPoint>(maxYears + 1)
        {
            new(0, balance),
        };

        int? yearsToFire = balance >= requiredAssets ? 0 : null;

        for (var year = 1; year <= maxYears; year++)
        {
            var eventCashFlow = SumEventsForYear(cashFlowEvents, year, scenario.InflationRate ?? 0m);
            balance = balance * (1m + scenario.ExpectedAnnualReturn) + scenario.AnnualSavings + eventCashFlow;
            path.Add(new FireWealthPoint(year, balance));

            if (yearsToFire is null && balance >= requiredAssets)
            {
                yearsToFire = year;
            }
        }

        if (yearsToFire is null)
        {
            warnings.Add(new FireProjectionWarning(
                FireProjectionWarningCode.UnableToReachFireWithinProjection,
                "目前假設在模擬期間內無法達成財務自由所需資產。"));
        }

        var projectedAtFire = yearsToFire.HasValue
            ? path[yearsToFire.Value].NetWorth
            : balance;

        return new FirePlanningProjection(
            RequiredAssets: requiredAssets,
            YearsToFire: yearsToFire,
            FireYear: yearsToFire.HasValue ? currentYear + yearsToFire.Value : null,
            ProjectedNetWorthAtFire: projectedAtFire,
            RequiredMonthlySavings: CalculateRequiredMonthlySavings(currentNetWorth, requiredAssets),
            MonteCarloSuccessRate: null,
            AccumulationPath: path,
            DrawdownPath: Array.Empty<FireDrawdownPoint>(),
            Warnings: warnings);
    }

    private static decimal SumEventsForYear(
        IReadOnlyList<FireCashFlowEvent> events,
        int year,
        decimal inflationRate)
    {
        var total = 0m;
        foreach (var item in events)
        {
            if (year < item.StartYearOffset)
                continue;
            if (item.EndYearOffset.HasValue && year > item.EndYearOffset.Value)
                continue;

            var amount = ApplyGrowth(item, year, inflationRate);
            total += item.Direction == FireCashFlowDirection.Inflow ? amount : -amount;
        }

        return total;
    }

    private static decimal ApplyGrowth(FireCashFlowEvent item, int year, decimal inflationRate)
    {
        var rate = item.GrowthMode switch
        {
            FireCashFlowGrowthMode.InflationAdjusted => inflationRate,
            FireCashFlowGrowthMode.CustomGrowthRate => item.CustomGrowthRate ?? 0m,
            _ => 0m,
        };

        var elapsedYears = Math.Max(0, year - item.StartYearOffset);
        var amount = item.AnnualAmount;
        for (var i = 0; i < elapsedYears; i++)
        {
            amount *= 1m + rate;
        }

        return amount;
    }

    private static decimal CalculateRequiredMonthlySavings(decimal currentNetWorth, decimal requiredAssets)
    {
        if (currentNetWorth >= requiredAssets)
            return 0m;

        return (requiredAssets - currentNetWorth) / 12m;
    }
}
