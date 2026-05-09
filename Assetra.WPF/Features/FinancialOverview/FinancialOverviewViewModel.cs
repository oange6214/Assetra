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
    private readonly ObservableCollection<KpiCardVm> _kpiCards = [];
    public ReadOnlyObservableCollection<KpiCardVm> KpiCards { get; }

    // ── KPI selector dialog state ─────────────────────────────────────────

    /// <summary>True while the gear-icon-triggered KPI edit dialog is open.</summary>
    [ObservableProperty]
    private bool _isKpiEditorOpen;

    /// <summary>
    /// Live editor list — one item per available metric, IsSelected toggled
    /// by checkboxes. Snapshotted from the persisted selection on open;
    /// flushed back to AppSettings on save.
    /// </summary>
    private readonly ObservableCollection<KpiSelectionItemVm> _kpiEditorItems = [];
    public ReadOnlyObservableCollection<KpiSelectionItemVm> KpiEditorItems { get; }

    [ObservableProperty]
    private int _kpiSelectedCount;

    [ObservableProperty]
    private bool _canSaveKpiSelection;

    public int KpiMinSelected => KpiMetricCatalog.MinSelected;
    public int KpiMaxSelected => KpiMetricCatalog.MaxSelected;

    // ── Accordion collections ─────────────────────────────────────────────

    private readonly ObservableCollection<AssetGroupVm> _assetGroups = [];
    private readonly ObservableCollection<AssetGroupVm> _investGroups = [];
    private readonly ObservableCollection<AssetGroupVm> _liabGroups = [];
    public ReadOnlyObservableCollection<AssetGroupVm> AssetGroups { get; }
    public ReadOnlyObservableCollection<AssetGroupVm> InvestGroups { get; }
    public ReadOnlyObservableCollection<AssetGroupVm> LiabGroups { get; }

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

        KpiCards = new ReadOnlyObservableCollection<KpiCardVm>(_kpiCards);
        KpiEditorItems = new ReadOnlyObservableCollection<KpiSelectionItemVm>(_kpiEditorItems);
        AssetGroups = new ReadOnlyObservableCollection<AssetGroupVm>(_assetGroups);
        InvestGroups = new ReadOnlyObservableCollection<AssetGroupVm>(_investGroups);
        LiabGroups = new ReadOnlyObservableCollection<AssetGroupVm>(_liabGroups);

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
            _assetGroups.Clear();
            foreach (var g in result.AssetGroups)
                _assetGroups.Add(ToGroupVm(g));

            _investGroups.Clear();
            foreach (var g in result.InvestmentGroups)
                _investGroups.Add(ToGroupVm(g));

            _liabGroups.Clear();
            foreach (var g in result.LiabilityGroups)
                _liabGroups.Add(ToGroupVm(g));

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
            vm.AddItem(new AssetItemVm { Id = item.Id, Name = item.Name, Currency = item.Currency, CurrentValue = item.CurrentValue });
        vm.Subtotal = group.Subtotal;
        return vm;
    }

    // ── KPI bar — user-selectable cards ──────────────────────────────────

    private void RebuildKpiCards()
    {
        // Triggered from multiple sources, some of which fire on background
        // threads (IPortfolioPositionFeed.PropertyChanged after a quote-fetch
        // worker, IAppSettingsService.Changed after a save Task). Marshal to
        // the UI thread before mutating the ObservableCollection — WPF's
        // CollectionView throws NotSupportedException on cross-thread edits.
        if (_uiContext is not null && SynchronizationContext.Current != _uiContext)
        {
            _uiContext.Post(_ => RebuildKpiCardsCore(), null);
            return;
        }
        RebuildKpiCardsCore();
    }

    private void RebuildKpiCardsCore()
    {
        var persisted = _settings?.Current.OverviewKpis;
        var ids = string.IsNullOrWhiteSpace(persisted)
            ? KpiMetricCatalog.Default
            : KpiMetricCatalog.ParseSelection(persisted.Split(','));

        _kpiCards.Clear();
        foreach (var id in ids)
            _kpiCards.Add(BuildCard(id));
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

    // ── KPI editor commands ──────────────────────────────────────────────

    [RelayCommand]
    private void OpenKpiEditor()
    {
        var current = string.IsNullOrWhiteSpace(_settings?.Current.OverviewKpis)
            ? KpiMetricCatalog.Default
            : KpiMetricCatalog.ParseSelection(_settings.Current.OverviewKpis.Split(','));
        var selectedSet = current.ToHashSet();

        _kpiEditorItems.Clear();
        foreach (var info in KpiMetricCatalog.All)
            _kpiEditorItems.Add(new KpiSelectionItemVm(info, selectedSet.Contains(info.Id), RecomputeKpiEditorState));

        RecomputeKpiEditorState();
        IsKpiEditorOpen = true;
    }

    [RelayCommand]
    private void CloseKpiEditor() => IsKpiEditorOpen = false;

    [RelayCommand(CanExecute = nameof(CanSaveKpiSelection))]
    private async Task SaveKpiSelectionAsync()
    {
        if (_settings is null) return;

        var selected = KpiEditorItems.Where(i => i.IsSelected).Select(i => i.Id).ToList();
        var serialised = KpiMetricCatalog.Serialize(selected);
        var next = _settings.Current with { OverviewKpis = serialised };
        await _settings.SaveAsync(next).ConfigureAwait(true);

        IsKpiEditorOpen = false;
        // RebuildKpiCards is triggered automatically by IAppSettingsService.Changed.
    }

    private void RecomputeKpiEditorState()
    {
        KpiSelectedCount = KpiEditorItems.Count(i => i.IsSelected);
        CanSaveKpiSelection = KpiSelectedCount >= KpiMetricCatalog.MinSelected
            && KpiSelectedCount <= KpiMetricCatalog.MaxSelected;
        SaveKpiSelectionCommand.NotifyCanExecuteChanged();
    }
}
