using Assetra.Core.Interfaces;
using Assetra.Core.Models;

namespace Assetra.Application.Portfolio.Dtos;

public sealed record PortfolioLoadResult(
    IReadOnlyList<PortfolioEntry> Entries,
    IReadOnlyDictionary<Guid, PositionSnapshot> PositionSnapshots,
    IReadOnlyList<Trade> Trades,
    IReadOnlyList<AssetItem> CashAccounts,
    IReadOnlyDictionary<Guid, decimal> CashBalances,
    IReadOnlyDictionary<string, LiabilitySnapshot> LiabilitySnapshots,
    IReadOnlyDictionary<string, AssetItem> LiabilityAssets);
