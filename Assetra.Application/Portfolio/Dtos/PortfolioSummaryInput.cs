using Assetra.Core.Models;

namespace Assetra.Application.Portfolio.Dtos;

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
