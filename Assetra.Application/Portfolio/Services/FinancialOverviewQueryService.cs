using Assetra.Application.Portfolio.Contracts;
using Assetra.Application.Portfolio.Dtos;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;

namespace Assetra.Application.Portfolio.Services;

public sealed class FinancialOverviewQueryService : IFinancialOverviewQueryService
{
    private readonly IAssetRepository _assetRepository;
    private readonly IBalanceQueryService _balanceQueryService;

    public FinancialOverviewQueryService(
        IAssetRepository assetRepository,
        IBalanceQueryService balanceQueryService)
    {
        _assetRepository = assetRepository;
        _balanceQueryService = balanceQueryService;
    }

    public async Task<FinancialOverviewResult> BuildAsync(
        IReadOnlyList<FinancialOverviewInvestmentItem> investments,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var groups = await _assetRepository.GetGroupsAsync().ConfigureAwait(false);
        var items = await _assetRepository.GetItemsAsync().ConfigureAwait(false);
        var cashBalances = await _balanceQueryService.GetAllCashBalancesAsync().ConfigureAwait(false);
        var liabilitySnapshots = await _balanceQueryService.GetAllLiabilitySnapshotsAsync().ConfigureAwait(false);

        var assetGroups = new List<FinancialOverviewGroup>();
        foreach (var group in groups.Where(g => g.Type == FinancialType.Asset).OrderBy(g => g.SortOrder))
        {
            var groupItems = new List<FinancialOverviewGroupItem>();
            foreach (var item in items.Where(i => i.GroupId == group.Id))
            {
                var value = await ResolveAssetValueAsync(item, cashBalances).ConfigureAwait(false);
                groupItems.Add(new FinancialOverviewGroupItem(item.Id, item.Name, item.Currency, value));
            }

            assetGroups.Add(new FinancialOverviewGroup(
                group.Icon ?? string.Empty,
                group.Name,
                groupItems,
                groupItems.Sum(i => i.CurrentValue)));
        }

        var ungroupedAssets = new List<FinancialOverviewGroupItem>();
        foreach (var item in items.Where(i => i.Type == FinancialType.Asset && i.GroupId is null))
        {
            var value = await ResolveAssetValueAsync(item, cashBalances).ConfigureAwait(false);
            ungroupedAssets.Add(new FinancialOverviewGroupItem(item.Id, item.Name, item.Currency, value));
        }
        if (ungroupedAssets.Count > 0)
        {
            assetGroups.Add(new FinancialOverviewGroup(
                "📁",
                "其他",
                ungroupedAssets,
                ungroupedAssets.Sum(i => i.CurrentValue)));
        }

        var investmentGroups = investments
            .GroupBy(i => i.AssetType)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var itemsInGroup = g.Select(i => new FinancialOverviewGroupItem(i.Id, i.Name, i.Currency, i.CurrentValue)).ToList();
                return new FinancialOverviewGroup(
                    GetInvestmentIcon(g.Key),
                    GetInvestmentName(g.Key),
                    itemsInGroup,
                    itemsInGroup.Sum(i => i.CurrentValue));
            })
            .ToList();

        var liabilityGroups = new List<FinancialOverviewGroup>();
        var activeLiabilities = liabilitySnapshots
            .Where(kv => kv.Value.Balance > 0)
            .OrderBy(kv => kv.Key)
            .Select(kv => new FinancialOverviewGroupItem(Guid.Empty, kv.Key, "TWD", kv.Value.Balance))
            .ToList();
        if (activeLiabilities.Count > 0)
        {
            liabilityGroups.Add(new FinancialOverviewGroup(
                "🏦",
                "貸款",
                activeLiabilities,
                activeLiabilities.Sum(i => i.CurrentValue)));
        }

        return new FinancialOverviewResult(
            assetGroups,
            investmentGroups,
            liabilityGroups,
            assetGroups.Sum(g => g.Subtotal),
            investmentGroups.Sum(g => g.Subtotal),
            liabilityGroups.Sum(g => g.Subtotal));
    }

    private async Task<decimal> ResolveAssetValueAsync(
        AssetItem item,
        IReadOnlyDictionary<Guid, decimal> cashBalances)
    {
        if (cashBalances.TryGetValue(item.Id, out var balance))
            return balance;

        var evt = await _assetRepository.GetLatestValuationAsync(item.Id).ConfigureAwait(false);
        return evt?.Amount ?? 0m;
    }

    private static string GetInvestmentIcon(AssetType assetType) => assetType switch
    {
        AssetType.Stock => "📈",
        AssetType.Fund => "📦",
        AssetType.Bond => "🏛",
        AssetType.PreciousMetal => "🥇",
        AssetType.Crypto => "🪙",
        _ => "📊",
    };

    private static string GetInvestmentName(AssetType assetType) => assetType switch
    {
        AssetType.Stock => "台股 / 個股",
        AssetType.Fund => "ETF / 基金",
        AssetType.Bond => "債券",
        AssetType.PreciousMetal => "貴金屬",
        AssetType.Crypto => "加密貨幣",
        _ => assetType.ToString(),
    };
}
