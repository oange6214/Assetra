using Assetra.Application.Portfolio.Contracts;
using Assetra.Application.Portfolio.Dtos;

namespace Assetra.Application.Portfolio.Services;

public sealed class PortfolioSummaryService : IPortfolioSummaryService
{
    public PortfolioSummaryResult Calculate(PortfolioSummaryInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var totalCost = input.Positions.Sum(p => p.Cost);
        var totalMarketValue = input.Positions.Sum(p => p.MarketValue);
        var totalPnl = input.Positions.Sum(p => p.MarketValue - p.Cost);
        var totalPnlPercent = totalCost > 0 ? totalPnl / totalCost * 100m : 0m;
        var isTotalPositive = totalPnl >= 0;

        var totalNetValue = input.Positions.Sum(p => p.NetValue);
        var positionWeights = input.Positions
            .Select(p => new PositionWeightResult(
                p.Id,
                totalNetValue > 0 ? p.NetValue / totalNetValue * 100m : 0m))
            .ToList();

        var totalCash = input.CashAccounts.Sum(c => c.Balance);
        var totalLiabilities = input.Liabilities.Sum(l => l.Balance);
        var totalAssets = totalMarketValue + totalCash;
        var netWorth = totalAssets - totalLiabilities;

        var priced = input.Positions
            .Where(p => !p.IsLoadingPrice && p.PrevClose > 0)
            .ToList();
        var hasDayPnl = priced.Count > 0;
        var dayPnl = priced.Sum(p => (p.CurrentPrice - p.PrevClose) * p.Quantity);
        var dayPnlBase = priced.Sum(p => p.PrevClose * p.Quantity);
        var dayPnlPercent = dayPnlBase > 0 ? dayPnl / dayPnlBase * 100m : 0m;
        var isDayPnlPositive = dayPnl >= 0;

        var totalOriginalLiabilities = input.Liabilities.Sum(l =>
            l.OriginalAmount > 0 ? l.OriginalAmount : l.Balance);
        var debtRatioValue = totalAssets > 0
            ? Math.Min(totalLiabilities / totalAssets * 100m, 100m)
            : 0m;
        var paidPercentValue = totalOriginalLiabilities > 0
            ? Math.Clamp((totalOriginalLiabilities - totalLiabilities) / totalOriginalLiabilities * 100m, 0m, 100m)
            : 0m;
        var emergencyFundMonths = input.MonthlyExpense > 0 ? totalCash / input.MonthlyExpense : 0m;

        var positionGroups = input.Positions
            .GroupBy(p => p.AssetType)
            .Select(g => new
            {
                AssetType = g.Key,
                Value = g.Sum(p => p.MarketValue > 0 ? p.MarketValue : p.Cost),
            })
            .Where(g => g.Value > 0)
            .ToList();

        var allocationTotal = positionGroups.Sum(g => g.Value)
            + Math.Max(0m, totalCash)
            + Math.Max(0m, totalLiabilities);

        var allocationSlices = new List<AllocationSliceResult>();
        if (allocationTotal > 0)
        {
            allocationSlices.AddRange(positionGroups.Select(g =>
                new AllocationSliceResult(
                    AllocationSliceKind.AssetType,
                    g.Value,
                    g.Value / allocationTotal * 100m,
                    g.AssetType)));

            if (totalCash > 0)
            {
                allocationSlices.Add(new AllocationSliceResult(
                    AllocationSliceKind.Cash,
                    totalCash,
                    totalCash / allocationTotal * 100m));
            }

            if (totalLiabilities > 0)
            {
                allocationSlices.Add(new AllocationSliceResult(
                    AllocationSliceKind.Liabilities,
                    totalLiabilities,
                    totalLiabilities / allocationTotal * 100m));
            }
        }

        return new PortfolioSummaryResult(
            totalCost,
            totalMarketValue,
            totalPnl,
            totalPnlPercent,
            isTotalPositive,
            positionWeights,
            totalCash,
            totalLiabilities,
            totalAssets,
            netWorth,
            hasDayPnl,
            dayPnl,
            dayPnlPercent,
            isDayPnlPositive,
            totalOriginalLiabilities,
            debtRatioValue,
            paidPercentValue,
            emergencyFundMonths,
            allocationSlices);
    }
}
