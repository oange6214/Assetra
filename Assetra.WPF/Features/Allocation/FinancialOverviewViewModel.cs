using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;

namespace Assetra.WPF.Features.Allocation;

/// <summary>
/// Financial Overview page — shows all Asset / Investment / Liability items
/// grouped by AssetGroup in an accordion layout.
///
/// Investments are read from <see cref="Portfolio.PortfolioViewModel"/> so live
/// price updates flow in automatically (no duplicate HTTP calls).
/// Cash/Liability balances are projected from the trade journal via
/// <see cref="IBalanceQueryService"/> (single source of truth).
/// Non-cash asset values (real estate, vehicles) use the most recent
/// AssetEvent.Valuation record from <see cref="IAssetRepository"/>.
/// </summary>
public sealed partial class FinancialOverviewViewModel : ObservableObject
{
    private readonly IAssetRepository     _assetRepo;
    private readonly IBalanceQueryService _balanceQuery;
    private readonly Portfolio.PortfolioViewModel _portfolio;

    // ── KPI bar ──────────────────────────────────────────────────────────

    [ObservableProperty] private decimal _totalNetWorth;
    [ObservableProperty] private decimal _totalAssets;
    [ObservableProperty] private decimal _totalInvestments;
    [ObservableProperty] private decimal _totalLiabilities;

    public string TotalNetWorthDisplay    => $"NT${TotalNetWorth:N0}";
    public string TotalAssetsDisplay      => $"NT${TotalAssets:N0}";
    public string TotalInvestmentsDisplay => $"NT${TotalInvestments:N0}";
    public string TotalLiabilitiesDisplay => $"NT${TotalLiabilities:N0}";

    partial void OnTotalNetWorthChanged(decimal _)    => OnPropertyChanged(nameof(TotalNetWorthDisplay));
    partial void OnTotalAssetsChanged(decimal _)      => OnPropertyChanged(nameof(TotalAssetsDisplay));
    partial void OnTotalInvestmentsChanged(decimal _) => OnPropertyChanged(nameof(TotalInvestmentsDisplay));
    partial void OnTotalLiabilitiesChanged(decimal _) => OnPropertyChanged(nameof(TotalLiabilitiesDisplay));

    // ── Accordion collections ─────────────────────────────────────────────

    public ObservableCollection<AssetGroupVm> AssetGroups  { get; } = [];
    public ObservableCollection<AssetGroupVm> InvestGroups { get; } = [];
    public ObservableCollection<AssetGroupVm> LiabGroups   { get; } = [];

    [ObservableProperty] private bool _isLoading;

    public FinancialOverviewViewModel(
        IAssetRepository assetRepo,
        IBalanceQueryService balanceQuery,
        Portfolio.PortfolioViewModel portfolio)
    {
        _assetRepo    = assetRepo    ?? throw new ArgumentNullException(nameof(assetRepo));
        _balanceQuery = balanceQuery ?? throw new ArgumentNullException(nameof(balanceQuery));
        _portfolio    = portfolio    ?? throw new ArgumentNullException(nameof(portfolio));
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        try
        {
            await LoadInternalAsync().ConfigureAwait(false);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadInternalAsync()
    {
        var groups    = await _assetRepo.GetGroupsAsync().ConfigureAwait(false);
        var items     = await _assetRepo.GetItemsAsync().ConfigureAwait(false);
        var cashBals  = await _balanceQuery.GetAllCashBalancesAsync().ConfigureAwait(false);
        var liabSnaps = await _balanceQuery.GetAllLiabilitySnapshotsAsync().ConfigureAwait(false);

        // ── Asset groups ──────────────────────────────────────────────────

        var assetGroups = groups
            .Where(g => g.Type == FinancialType.Asset)
            .OrderBy(g => g.SortOrder)
            .ToList();

        var assetGroupVms = new List<AssetGroupVm>();
        foreach (var g in assetGroups)
        {
            var groupItems = items.Where(i => i.GroupId == g.Id).ToList();
            var vm = new AssetGroupVm { Icon = g.Icon ?? string.Empty, Name = g.Name };
            foreach (var item in groupItems)
            {
                decimal value;
                if (cashBals.TryGetValue(item.Id, out var bal))
                {
                    value = bal;  // bank accounts: trade-journal projection
                }
                else
                {
                    // Non-cash asset: use latest Valuation event
                    var evt = await _assetRepo.GetLatestValuationAsync(item.Id).ConfigureAwait(false);
                    value = evt?.Amount ?? 0m;
                }
                vm.Items.Add(new AssetItemVm { Id = item.Id, Name = item.Name, Currency = item.Currency, CurrentValue = value });
            }
            vm.Subtotal = vm.Items.Sum(i => i.CurrentValue);
            assetGroupVms.Add(vm);
        }

        // Ungrouped asset items
        var ungroupedAssets = items
            .Where(i => i.Type == FinancialType.Asset && i.GroupId is null)
            .ToList();
        if (ungroupedAssets.Count > 0)
        {
            var ungrouped = new AssetGroupVm { Icon = "📁", Name = "其他" };
            foreach (var item in ungroupedAssets)
            {
                var evt = await _assetRepo.GetLatestValuationAsync(item.Id).ConfigureAwait(false);
                var value = cashBals.TryGetValue(item.Id, out var bal) ? bal : evt?.Amount ?? 0m;
                ungrouped.Items.Add(new AssetItemVm { Id = item.Id, Name = item.Name, Currency = item.Currency, CurrentValue = value });
            }
            ungrouped.Subtotal = ungrouped.Items.Sum(i => i.CurrentValue);
            assetGroupVms.Add(ungrouped);
        }

        // ── Investment groups (from PortfolioViewModel) ───────────────────

        var investGroupsRaw = new List<AssetGroupVm>();
        var positionsByType = _portfolio.Positions
            .GroupBy(p => p.AssetType)
            .OrderBy(g => g.Key);

        foreach (var typeGroup in positionsByType)
        {
            var assetType = typeGroup.Key;
            var gvm = new AssetGroupVm
            {
                Icon = assetType switch
                {
                    AssetType.Stock        => "📈",
                    AssetType.Fund         => "📦",
                    AssetType.Bond         => "🏛",
                    AssetType.PreciousMetal => "🥇",
                    AssetType.Crypto       => "🪙",
                    _                      => "📊",
                },
                Name = assetType switch
                {
                    AssetType.Stock        => "台股 / 個股",
                    AssetType.Fund         => "ETF / 基金",
                    AssetType.Bond         => "債券",
                    AssetType.PreciousMetal => "貴金屬",
                    AssetType.Crypto       => "加密貨幣",
                    _                      => assetType.ToString(),
                },
            };

            foreach (var pos in typeGroup)
                gvm.Items.Add(new AssetItemVm { Id = pos.Id, Name = pos.Name, Currency = pos.Currency, CurrentValue = pos.MarketValue });

            gvm.Subtotal = gvm.Items.Sum(i => i.CurrentValue);
            investGroupsRaw.Add(gvm);
        }

        // ── Liability groups: derived from trade projections (no asset entity needed) ──────
        var liabGroupVms = new List<AssetGroupVm>();
        var activeLiabs = liabSnaps
            .Where(kv => kv.Value.Balance > 0)
            .OrderBy(kv => kv.Key)
            .ToList();
        if (activeLiabs.Count > 0)
        {
            var loanGroup = new AssetGroupVm { Icon = "🏦", Name = "貸款" };
            foreach (var (label, snap) in activeLiabs)
                loanGroup.Items.Add(new AssetItemVm
                {
                    Id = Guid.Empty,
                    Name = label,
                    Currency = "TWD",
                    CurrentValue = snap.Balance,
                });
            loanGroup.Subtotal = loanGroup.Items.Sum(i => i.CurrentValue);
            liabGroupVms.Add(loanGroup);
        }

        // ── Update collections on UI thread ──────────────────────────────

        var totalAssets      = assetGroupVms.Sum(g => g.Subtotal);
        var totalInvestments = investGroupsRaw.Sum(g => g.Subtotal);
        var totalLiabilities = liabGroupVms.Sum(g => g.Subtotal);

        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            AssetGroups.Clear();
            foreach (var g in assetGroupVms) AssetGroups.Add(g);

            InvestGroups.Clear();
            foreach (var g in investGroupsRaw) InvestGroups.Add(g);

            LiabGroups.Clear();
            foreach (var g in liabGroupVms) LiabGroups.Add(g);

            TotalAssets      = totalAssets;
            TotalInvestments = totalInvestments;
            TotalLiabilities = totalLiabilities;
            TotalNetWorth    = totalAssets + totalInvestments - totalLiabilities;
        });
    }
}
