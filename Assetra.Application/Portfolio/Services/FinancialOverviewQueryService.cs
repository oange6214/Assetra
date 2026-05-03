using Assetra.Application.Portfolio.Contracts;
using Assetra.Application.Portfolio.Dtos;
using Assetra.Core.Interfaces;
using Assetra.Core.Interfaces.Analysis;
using Assetra.Core.Models;

namespace Assetra.Application.Portfolio.Services;

public sealed class FinancialOverviewQueryService : IFinancialOverviewQueryService
{
    private readonly IAssetRepository _assetRepository;
    private readonly IBalanceQueryService _balanceQueryService;
    private readonly IMultiCurrencyValuationService? _fx;
    private readonly IAppSettingsService? _settings;

    public FinancialOverviewQueryService(
        IAssetRepository assetRepository,
        IBalanceQueryService balanceQueryService,
        IMultiCurrencyValuationService? fx = null,
        IAppSettingsService? settings = null)
    {
        _assetRepository = assetRepository;
        _balanceQueryService = balanceQueryService;
        _fx = fx;
        _settings = settings;
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
        var baseCurrency = ResolveBaseCurrency();
        var asOf = DateOnly.FromDateTime(DateTime.Today);

        // Batch valuation lookup for all non-cash asset items in one SQL round-trip.
        var nonCashAssetIds = items
            .Where(i => i.Type == FinancialType.Asset && !cashBalances.ContainsKey(i.Id))
            .Select(i => i.Id);
        var valuations = await _assetRepository.GetLatestValuationsAsync(nonCashAssetIds, ct).ConfigureAwait(false);

        var assetGroups = new List<FinancialOverviewGroup>();
        foreach (var group in groups.Where(g => g.Type == FinancialType.Asset).OrderBy(g => g.SortOrder))
        {
            var groupItems = new List<FinancialOverviewGroupItem>();
            foreach (var item in items.Where(i => i.GroupId == group.Id))
            {
                var value = ResolveAssetValue(item, cashBalances, valuations);
                var converted = await ConvertToBaseAsync(value, item.Currency, baseCurrency, asOf, ct).ConfigureAwait(false);
                groupItems.Add(new FinancialOverviewGroupItem(item.Id, item.Name, baseCurrency, converted));
            }

            assetGroups.Add(new FinancialOverviewGroup(
                group.Icon ?? string.Empty,
                group.Name,
                baseCurrency,
                groupItems,
                groupItems.Sum(i => i.CurrentValue)));
        }

        var ungroupedAssets = new List<FinancialOverviewGroupItem>();
        foreach (var item in items.Where(i => i.Type == FinancialType.Asset && i.GroupId is null))
        {
            var value = ResolveAssetValue(item, cashBalances, valuations);
            var converted = await ConvertToBaseAsync(value, item.Currency, baseCurrency, asOf, ct).ConfigureAwait(false);
            ungroupedAssets.Add(new FinancialOverviewGroupItem(item.Id, item.Name, baseCurrency, converted));
        }
        if (ungroupedAssets.Count > 0)
        {
            assetGroups.Add(new FinancialOverviewGroup(
                "📁",
                "其他",
                baseCurrency,
                ungroupedAssets,
                ungroupedAssets.Sum(i => i.CurrentValue)));
        }

        var convertedInvestments = new List<FinancialOverviewInvestmentItem>(investments.Count);
        foreach (var investment in investments)
        {
            convertedInvestments.Add(investment with
            {
                Currency = baseCurrency,
                CurrentValue = await ConvertToBaseAsync(
                    investment.CurrentValue,
                    investment.Currency,
                    baseCurrency,
                    asOf,
                    ct).ConfigureAwait(false),
            });
        }

        var investmentGroups = convertedInvestments
            .GroupBy(i => i.AssetType)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var itemsInGroup = g.Select(i => new FinancialOverviewGroupItem(i.Id, i.Name, i.Currency, i.CurrentValue)).ToList();
                return new FinancialOverviewGroup(
                    GetInvestmentIcon(g.Key),
                    GetInvestmentName(g.Key),
                    baseCurrency,
                    itemsInGroup,
                    itemsInGroup.Sum(i => i.CurrentValue));
            })
            .ToList();

        var liabilityGroups = await BuildLiabilityGroupsAsync(items, liabilitySnapshots, baseCurrency, asOf, ct)
            .ConfigureAwait(false);

        return new FinancialOverviewResult(
            assetGroups,
            investmentGroups,
            liabilityGroups,
            baseCurrency,
            assetGroups.Sum(g => g.Subtotal),
            investmentGroups.Sum(g => g.Subtotal),
            liabilityGroups.Sum(g => g.Subtotal));
    }

    private static decimal ResolveAssetValue(
        AssetItem item,
        IReadOnlyDictionary<Guid, decimal> cashBalances,
        IReadOnlyDictionary<Guid, AssetEvent> valuations)
    {
        if (cashBalances.TryGetValue(item.Id, out var balance))
            return balance;
        return valuations.TryGetValue(item.Id, out var evt) ? evt.Amount ?? 0m : 0m;
    }

    private async Task<decimal> ConvertToBaseAsync(
        decimal amount,
        string currency,
        string baseCurrency,
        DateOnly asOf,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(currency)
            || string.Equals(currency, baseCurrency, StringComparison.OrdinalIgnoreCase)
            || _fx is null)
        {
            return amount;
        }

        return await _fx.ConvertAsync(amount, currency, baseCurrency, asOf, ct).ConfigureAwait(false)
            ?? amount;
    }

    private string ResolveBaseCurrency()
    {
        var baseCurrency = _settings?.Current.BaseCurrency;
        return string.IsNullOrWhiteSpace(baseCurrency) ? "TWD" : baseCurrency;
    }

    private async Task<List<FinancialOverviewGroup>> BuildLiabilityGroupsAsync(
        IReadOnlyList<AssetItem> items,
        IReadOnlyDictionary<string, LiabilitySnapshot> liabilitySnapshots,
        string baseCurrency,
        DateOnly asOf,
        CancellationToken ct)
    {
        var liabilityAssets = items
            .Where(i => i.Type == FinancialType.Liability)
            .ToDictionary(i => i.Name, StringComparer.Ordinal);

        var rows = new List<(AssetItem? Asset, string Label, decimal Balance)>();
        foreach (var (label, snapshot) in liabilitySnapshots.OrderBy(kv => kv.Key))
        {
            liabilityAssets.TryGetValue(label, out var asset);
            rows.Add((asset, label, snapshot.Balance));
        }

        foreach (var (name, asset) in liabilityAssets.OrderBy(kv => kv.Key))
        {
            if (!liabilitySnapshots.ContainsKey(name))
                rows.Add((asset, name, 0m));
        }

        var groups = new List<FinancialOverviewGroup>();
        foreach (var group in rows.GroupBy(r => GetLiabilityGroup(r.Asset)).OrderBy(g => g.Key.SortOrder))
        {
            var groupItems = new List<FinancialOverviewGroupItem>();
            foreach (var row in group.OrderBy(r => r.Label))
            {
                var currency = row.Asset?.Currency ?? baseCurrency;
                var converted = await ConvertToBaseAsync(row.Balance, currency, baseCurrency, asOf, ct)
                    .ConfigureAwait(false);
                groupItems.Add(new FinancialOverviewGroupItem(
                    row.Asset?.Id ?? Guid.Empty,
                    row.Label,
                    baseCurrency,
                    converted));
            }

            groups.Add(new FinancialOverviewGroup(
                group.Key.Icon,
                group.Key.Name,
                baseCurrency,
                groupItems,
                groupItems.Sum(i => i.CurrentValue)));
        }

        return groups;
    }

    private static (int SortOrder, string Icon, string Name) GetLiabilityGroup(AssetItem? asset)
    {
        if (asset?.IsCreditCard == true)
            return (1, "💳", "信用卡");
        if (asset?.IsLoan == true || asset is null)
            return (0, "🏦", "貸款");
        return (2, "📄", "其他負債");
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
