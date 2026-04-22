using Assetra.Core.Models;

namespace Assetra.Core.Dtos;

public sealed record PortfolioSummaryInput(
    IReadOnlyList<PositionSummaryInput> Positions,
    IReadOnlyList<CashBalanceInput> CashAccounts,
    IReadOnlyList<LiabilityBalanceInput> Liabilities,
    decimal MonthlyExpense);

public sealed record PositionSummaryInput(
    Guid Id,
    AssetType AssetType,
    decimal Quantity,
    decimal Cost,
    decimal MarketValue,
    decimal NetValue,
    decimal CurrentPrice,
    decimal PrevClose,
    bool IsLoadingPrice);

public sealed record CashBalanceInput(Guid Id, decimal Balance);

public sealed record LiabilityBalanceInput(Guid Id, decimal Balance, decimal OriginalAmount);

public sealed record PortfolioSummaryResult(
    decimal TotalCost,
    decimal TotalMarketValue,
    decimal TotalPnl,
    decimal TotalPnlPercent,
    bool IsTotalPositive,
    IReadOnlyList<PositionWeightResult> PositionWeights,
    decimal TotalCash,
    decimal TotalLiabilities,
    decimal TotalAssets,
    decimal NetWorth,
    bool HasDayPnl,
    decimal DayPnl,
    decimal DayPnlPercent,
    bool IsDayPnlPositive,
    decimal TotalOriginalLiabilities,
    decimal DebtRatioValue,
    decimal PaidPercentValue,
    decimal EmergencyFundMonths,
    IReadOnlyList<AllocationSliceResult> AllocationSlices);

public sealed record PositionWeightResult(Guid PositionId, decimal Percent);

public sealed record AllocationSliceResult(
    AllocationSliceKind Kind,
    decimal Value,
    decimal Percent,
    AssetType? AssetType = null);

public enum AllocationSliceKind
{
    AssetType,
    Cash,
    Liabilities,
}
