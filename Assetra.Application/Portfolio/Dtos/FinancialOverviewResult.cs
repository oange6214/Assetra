using Assetra.Core.Models;

namespace Assetra.Application.Portfolio.Dtos;

public sealed record FinancialOverviewInvestmentItem(
    Guid Id,
    string Name,
    string Currency,
    decimal CurrentValue,
    AssetType AssetType);

public sealed record FinancialOverviewGroupItem(
    Guid Id,
    string Name,
    string Currency,
    decimal CurrentValue);

public sealed record FinancialOverviewGroup(
    string Icon,
    string Name,
    string Currency,
    IReadOnlyList<FinancialOverviewGroupItem> Items,
    decimal Subtotal);

public sealed record FinancialOverviewResult(
    IReadOnlyList<FinancialOverviewGroup> AssetGroups,
    IReadOnlyList<FinancialOverviewGroup> InvestmentGroups,
    IReadOnlyList<FinancialOverviewGroup> LiabilityGroups,
    string BaseCurrency,
    decimal TotalAssets,
    decimal TotalInvestments,
    decimal TotalLiabilities);
