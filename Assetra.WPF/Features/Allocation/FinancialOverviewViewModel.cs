using System.Collections.ObjectModel;
using Assetra.AppLayer.Portfolio.Contracts;
using Assetra.AppLayer.Portfolio.Dtos;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

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
    private readonly IFinancialOverviewQueryService _queryService;
    private readonly Portfolio.PortfolioViewModel _portfolio;

    // ── KPI bar ──────────────────────────────────────────────────────────

    [ObservableProperty] private decimal _totalNetWorth;
    [ObservableProperty] private decimal _totalAssets;
    [ObservableProperty] private decimal _totalInvestments;
    [ObservableProperty] private decimal _totalLiabilities;

    public string TotalNetWorthDisplay => $"NT${TotalNetWorth:N0}";
    public string TotalAssetsDisplay => $"NT${TotalAssets:N0}";
    public string TotalInvestmentsDisplay => $"NT${TotalInvestments:N0}";
    public string TotalLiabilitiesDisplay => $"NT${TotalLiabilities:N0}";

    partial void OnTotalNetWorthChanged(decimal _) => OnPropertyChanged(nameof(TotalNetWorthDisplay));
    partial void OnTotalAssetsChanged(decimal _) => OnPropertyChanged(nameof(TotalAssetsDisplay));
    partial void OnTotalInvestmentsChanged(decimal _) => OnPropertyChanged(nameof(TotalInvestmentsDisplay));
    partial void OnTotalLiabilitiesChanged(decimal _) => OnPropertyChanged(nameof(TotalLiabilitiesDisplay));

    // ── Accordion collections ─────────────────────────────────────────────

    public ObservableCollection<AssetGroupVm> AssetGroups { get; } = [];
    public ObservableCollection<AssetGroupVm> InvestGroups { get; } = [];
    public ObservableCollection<AssetGroupVm> LiabGroups { get; } = [];

    [ObservableProperty] private bool _isLoading;

    public FinancialOverviewViewModel(
        IFinancialOverviewQueryService queryService,
        Portfolio.PortfolioViewModel portfolio)
    {
        _queryService = queryService ?? throw new ArgumentNullException(nameof(queryService));
        _portfolio = portfolio ?? throw new ArgumentNullException(nameof(portfolio));
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        if (IsLoading)
            return;
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
        var result = await _queryService.BuildAsync(
            _portfolio.Positions
                .Select(p => new FinancialOverviewInvestmentItem(
                    p.Id,
                    p.Name,
                    p.Currency,
                    p.MarketValue,
                    p.AssetType))
                .ToList())
            .ConfigureAwait(false);

        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            AssetGroups.Clear();
            foreach (var g in result.AssetGroups)
                AssetGroups.Add(ToGroupVm(g));

            InvestGroups.Clear();
            foreach (var g in result.InvestmentGroups)
                InvestGroups.Add(ToGroupVm(g));

            LiabGroups.Clear();
            foreach (var g in result.LiabilityGroups)
                LiabGroups.Add(ToGroupVm(g));

            TotalAssets = result.TotalAssets;
            TotalInvestments = result.TotalInvestments;
            TotalLiabilities = result.TotalLiabilities;
            TotalNetWorth = result.TotalAssets + result.TotalInvestments - result.TotalLiabilities;
        });
    }

    private static AssetGroupVm ToGroupVm(FinancialOverviewGroup group)
    {
        var vm = new AssetGroupVm { Icon = group.Icon, Name = group.Name };
        foreach (var item in group.Items)
            vm.Items.Add(new AssetItemVm { Id = item.Id, Name = item.Name, Currency = item.Currency, CurrentValue = item.CurrentValue });
        vm.Subtotal = group.Subtotal;
        return vm;
    }
}
