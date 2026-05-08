using System.Collections.ObjectModel;
using System.Globalization;
using Assetra.WPF.Features.Portfolio.Contracts;
using Assetra.WPF.Infrastructure;
using Assetra.Application.Portfolio.Contracts;
using Assetra.Application.Portfolio.Dtos;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Assetra.WPF.Features.FinancialOverview;

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
    private readonly IPortfolioPositionFeed _portfolio;
    private readonly SynchronizationContext? _uiContext;

    // ── KPI bar ──────────────────────────────────────────────────────────

    [ObservableProperty] private decimal _totalNetWorth;
    [ObservableProperty] private decimal _totalAssets;
    [ObservableProperty] private decimal _totalInvestments;
    [ObservableProperty] private decimal _totalLiabilities;
    [ObservableProperty] private string _baseCurrency = "TWD";

    public string TotalNetWorthDisplay => MoneyFormatter.Format(TotalNetWorth, BaseCurrency);
    public string TotalAssetsDisplay => MoneyFormatter.Format(TotalAssets, BaseCurrency);
    public string TotalInvestmentsDisplay => MoneyFormatter.Format(TotalInvestments, BaseCurrency);
    public string TotalLiabilitiesDisplay => MoneyFormatter.Format(TotalLiabilities, BaseCurrency);

    partial void OnTotalNetWorthChanged(decimal _) { OnPropertyChanged(nameof(TotalNetWorthDisplay)); RebuildKpiCards(); }
    partial void OnTotalAssetsChanged(decimal _) { OnPropertyChanged(nameof(TotalAssetsDisplay)); RebuildKpiCards(); }
    partial void OnTotalInvestmentsChanged(decimal _) { OnPropertyChanged(nameof(TotalInvestmentsDisplay)); RebuildKpiCards(); }
    partial void OnTotalLiabilitiesChanged(decimal _) { OnPropertyChanged(nameof(TotalLiabilitiesDisplay)); RebuildKpiCards(); }
    partial void OnBaseCurrencyChanged(string _)
    {
        OnPropertyChanged(nameof(TotalNetWorthDisplay));
        OnPropertyChanged(nameof(TotalAssetsDisplay));
        OnPropertyChanged(nameof(TotalInvestmentsDisplay));
        OnPropertyChanged(nameof(TotalLiabilitiesDisplay));
        RebuildKpiCards();
    }

    /// <summary>
    /// Snapshot of the KPI bar cards in user-selected order. Rebuilt whenever
    /// the underlying totals or the user's <see cref="AppSettings.OverviewKpis"/>
    /// selection changes. Bound to an ItemsControl in the view.
    /// </summary>
    public ObservableCollection<KpiCardVm> KpiCards { get; } = [];

    // ── Accordion collections ─────────────────────────────────────────────

    public ObservableCollection<AssetGroupVm> AssetGroups { get; } = [];
    public ObservableCollection<AssetGroupVm> InvestGroups { get; } = [];
    public ObservableCollection<AssetGroupVm> LiabGroups { get; } = [];

    [ObservableProperty] private bool _isLoading;

    private readonly IAppSettingsService? _settings;

    public FinancialOverviewViewModel(
        IFinancialOverviewQueryService queryService,
        IPortfolioPositionFeed portfolio,
        IAppSettingsService? settings = null)
    {
        _queryService = queryService ?? throw new ArgumentNullException(nameof(queryService));
        _portfolio = portfolio ?? throw new ArgumentNullException(nameof(portfolio));
        _settings = settings;
        _uiContext = SynchronizationContext.Current;

        // Stock prices arrive asynchronously after Portfolio.LoadAsync() completes.
        // Subscribing to TotalMarketValue lets FinancialOverview re-snapshot once
        // the first batch of live prices lands; without this hook the Investments
        // section freezes at TWD 0 because LoadAsync was called pre-price-fetch.
        _portfolio.PropertyChanged += OnPortfolioPropertyChanged;

        if (_settings is not null)
            _settings.Changed += RebuildKpiCards;

        RebuildKpiCards();
    }

    private void OnPortfolioPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IPortfolioPositionFeed.TotalMarketValue))
            AsyncHelpers.SafeFireAndForget(LoadAsync, "FinancialOverview.Load");

        // Investment P&L KPI cards depend on both TotalMarketValue and TotalCost.
        // Rebuild on either so the card refreshes when price quotes land or when
        // a new buy/sell shifts the cost basis.
        if (e.PropertyName == nameof(IPortfolioPositionFeed.TotalMarketValue)
            || e.PropertyName == nameof(IPortfolioPositionFeed.TotalCost))
            RebuildKpiCards();
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        if (IsLoading)
            return;
        IsLoading = true;
        try
        {
            await LoadInternalAsync().ConfigureAwait(true);
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
            .ConfigureAwait(true);

        void ApplyResult()
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

            BaseCurrency = result.BaseCurrency;
            TotalAssets = result.TotalAssets;
            TotalInvestments = result.TotalInvestments;
            TotalLiabilities = result.TotalLiabilities;
            TotalNetWorth = result.TotalAssets + result.TotalInvestments - result.TotalLiabilities;
        }

        if (_uiContext is null || SynchronizationContext.Current == _uiContext)
        {
            ApplyResult();
            return;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _uiContext.Post(_ =>
        {
            try
            {
                ApplyResult();
                completion.SetResult();
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        }, null);
        await completion.Task.ConfigureAwait(false);
    }

    private static AssetGroupVm ToGroupVm(FinancialOverviewGroup group)
    {
        var vm = new AssetGroupVm { Icon = group.Icon, Name = group.Name, Currency = group.Currency };
        foreach (var item in group.Items)
            vm.Items.Add(new AssetItemVm { Id = item.Id, Name = item.Name, Currency = item.Currency, CurrentValue = item.CurrentValue });
        vm.Subtotal = group.Subtotal;
        return vm;
    }

    // ── KPI bar — user-selectable cards ──────────────────────────────────

    private void RebuildKpiCards()
    {
        var persisted = _settings?.Current.OverviewKpis;
        var ids = string.IsNullOrWhiteSpace(persisted)
            ? KpiMetricCatalog.Default
            : KpiMetricCatalog.ParseSelection(persisted.Split(','));

        KpiCards.Clear();
        foreach (var id in ids)
            KpiCards.Add(BuildCard(id));
    }

    private KpiCardVm BuildCard(KpiMetric id)
    {
        var info = KpiMetricCatalog.All.First(m => m.Id == id);
        return id switch
        {
            KpiMetric.NetWorth =>
                Money(id, info.LabelKey, TotalNetWorth, KpiTone.Neutral),
            KpiMetric.TotalAssets =>
                Money(id, info.LabelKey, TotalAssets + TotalInvestments, KpiTone.Up),
            KpiMetric.OtherAssets =>
                Money(id, info.LabelKey, TotalAssets, KpiTone.Neutral),
            KpiMetric.Investments =>
                Money(id, info.LabelKey, TotalInvestments, KpiTone.Accent),
            KpiMetric.Liabilities =>
                Money(id, info.LabelKey, TotalLiabilities, KpiTone.Down),
            KpiMetric.DebtRatio =>
                Ratio(id, info.LabelKey, TotalLiabilities, TotalAssets + TotalInvestments, KpiTone.Neutral),
            KpiMetric.InvestmentPnl =>
                Money(id, info.LabelKey,
                    _portfolio.TotalMarketValue - _portfolio.TotalCost,
                    SignTone(_portfolio.TotalMarketValue - _portfolio.TotalCost)),
            KpiMetric.InvestmentPnlPercent =>
                Percent(id, info.LabelKey,
                    _portfolio.TotalCost == 0m ? 0m
                        : (_portfolio.TotalMarketValue - _portfolio.TotalCost) / _portfolio.TotalCost,
                    SignTone(_portfolio.TotalMarketValue - _portfolio.TotalCost)),
            _ => new KpiCardVm(id, info.LabelKey, "—", KpiTone.Neutral),
        };
    }

    private KpiCardVm Money(KpiMetric id, string labelKey, decimal value, KpiTone tone) =>
        new(id, labelKey, MoneyFormatter.Format(value, BaseCurrency), tone);

    private static KpiCardVm Ratio(KpiMetric id, string labelKey, decimal numerator, decimal denominator, KpiTone tone)
    {
        var pct = denominator == 0m ? 0m : numerator / denominator * 100m;
        return new KpiCardVm(id, labelKey, pct.ToString("F1", CultureInfo.InvariantCulture) + "%", tone);
    }

    private static KpiCardVm Percent(KpiMetric id, string labelKey, decimal fraction, KpiTone tone)
    {
        var sign = fraction >= 0m ? "+" : string.Empty;
        return new KpiCardVm(id, labelKey, sign + (fraction * 100m).ToString("F2", CultureInfo.InvariantCulture) + "%", tone);
    }

    private static KpiTone SignTone(decimal value) =>
        value > 0m ? KpiTone.Up
        : value < 0m ? KpiTone.Down
        : KpiTone.Neutral;
}
