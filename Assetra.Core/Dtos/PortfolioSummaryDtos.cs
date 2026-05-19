using Assetra.Core.Models;

namespace Assetra.Core.Dtos;

public sealed record PortfolioSummaryInput(
    IReadOnlyList<PositionSummaryInput> Positions,
    IReadOnlyList<CashBalanceInput> CashAccounts,
    IReadOnlyList<LiabilityBalanceInput> Liabilities,
    decimal MonthlyExpense,
    string BaseCurrency = "TWD");

public sealed record PositionSummaryInput(
    Guid Id,
    AssetType AssetType,
    decimal Quantity,
    decimal Cost,
    decimal MarketValue,
    decimal NetValue,
    decimal CurrentPrice,
    decimal PrevClose,
    bool IsLoadingPrice,
    string NativeCurrency = "",
    string BaseCurrency = "",
    decimal NativeCost = 0m,
    decimal NativeMarketValue = 0m,
    decimal NativeNetValue = 0m,
    decimal NativeCurrentPrice = 0m,
    decimal NativePrevClose = 0m)
{
    public decimal BaseCost => Cost;
    public decimal BaseMarketValue => MarketValue;
    public decimal BaseNetValue => NetValue;
    public decimal BaseCurrentPrice => CurrentPrice;
    public decimal BasePrevClose => PrevClose;

    public Money NativeCostMoney => new(NativeCost, ResolveCurrency(NativeCurrency, BaseCurrency));
    public Money NativeMarketValueMoney => new(NativeMarketValue, ResolveCurrency(NativeCurrency, BaseCurrency));
    public Money NativeNetValueMoney => new(NativeNetValue, ResolveCurrency(NativeCurrency, BaseCurrency));
    public Money NativeCurrentPriceMoney => new(NativeCurrentPrice, ResolveCurrency(NativeCurrency, BaseCurrency));
    public Money NativePrevCloseMoney => new(NativePrevClose, ResolveCurrency(NativeCurrency, BaseCurrency));

    public Money BaseCostMoney => new(BaseCost, ResolveCurrency(BaseCurrency, NativeCurrency));
    public Money BaseMarketValueMoney => new(BaseMarketValue, ResolveCurrency(BaseCurrency, NativeCurrency));
    public Money BaseNetValueMoney => new(BaseNetValue, ResolveCurrency(BaseCurrency, NativeCurrency));
    public Money BaseCurrentPriceMoney => new(BaseCurrentPrice, ResolveCurrency(BaseCurrency, NativeCurrency));
    public Money BasePrevCloseMoney => new(BasePrevClose, ResolveCurrency(BaseCurrency, NativeCurrency));

    private static string ResolveCurrency(string primary, string fallback) =>
        !string.IsNullOrWhiteSpace(primary)
            ? primary
            : !string.IsNullOrWhiteSpace(fallback) ? fallback : "TWD";
}

public sealed record CashBalanceInput(
    Guid Id,
    decimal Balance,
    string NativeCurrency = "",
    string BaseCurrency = "",
    decimal NativeBalance = 0m)
{
    public decimal BaseBalance => Balance;
    public Money NativeBalanceMoney => new(
        NativeBalance,
        !string.IsNullOrWhiteSpace(NativeCurrency)
            ? NativeCurrency
            : !string.IsNullOrWhiteSpace(BaseCurrency) ? BaseCurrency : "TWD");

    public Money BaseBalanceMoney => new(
        BaseBalance,
        !string.IsNullOrWhiteSpace(BaseCurrency)
            ? BaseCurrency
            : !string.IsNullOrWhiteSpace(NativeCurrency) ? NativeCurrency : "TWD");
}

public sealed record LiabilityBalanceInput(
    Guid Id,
    decimal Balance,
    decimal OriginalAmount,
    string NativeCurrency = "",
    string BaseCurrency = "",
    decimal NativeBalance = 0m,
    decimal NativeOriginalAmount = 0m)
{
    public decimal BaseBalance => Balance;
    public decimal BaseOriginalAmount => OriginalAmount;
    public Money NativeBalanceMoney => new(NativeBalance, ResolveCurrency(NativeCurrency, BaseCurrency));
    public Money NativeOriginalAmountMoney => new(NativeOriginalAmount, ResolveCurrency(NativeCurrency, BaseCurrency));
    public Money BaseBalanceMoney => new(BaseBalance, ResolveCurrency(BaseCurrency, NativeCurrency));
    public Money BaseOriginalAmountMoney => new(BaseOriginalAmount, ResolveCurrency(BaseCurrency, NativeCurrency));

    private static string ResolveCurrency(string primary, string fallback) =>
        !string.IsNullOrWhiteSpace(primary)
            ? primary
            : !string.IsNullOrWhiteSpace(fallback) ? fallback : "TWD";
}

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
