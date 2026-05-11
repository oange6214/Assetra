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

    /// <summary>
    /// Stage 2 (Dashboard consolidation)：4-tab 儀表板的 active tab。
    /// 由 NavRailView 的 FinancialOverviewContentTemplate 內的 TabControl
    /// 透過 SelectedValue / Tag 雙向綁定。預設停留在「總覽」。
    /// </summary>
    [ObservableProperty] private DashboardTab _selectedDashboardTab = DashboardTab.Overview;

    private readonly IAppSettingsService? _settings;

    /// <summary>
    /// Stage 2.5 (Dashboard consolidation)：總覽 tab 上的 widget 直接綁這三個 VM。
    /// optional 注入，測試 / design-time 可省略。widget 不允許從首頁編輯，
    /// 只投影 + 點擊跳轉。
    /// </summary>
    public Assetra.WPF.Features.Goals.GoalsViewModel? GoalsWidget { get; }
    public Assetra.WPF.Features.Fire.FireViewModel? FireWidget { get; }
    public Assetra.WPF.Features.Assistant.AssistantViewModel? AssistantWidget { get; }

    /// <summary>
    /// Stage 2.5 polish：總覽 widget 顯示的前 3 個目標 — 按 deadline 升冪
    /// （無 deadline 排最後）。GoalsWidget.Goals 集合變動時自動 refresh。
    /// </summary>
    public IEnumerable<Assetra.WPF.Features.Goals.GoalRowViewModel> TopThreeGoals =>
        GoalsWidget is null
            ? []
            : GoalsWidget.Goals
                .OrderBy(g => g.Goal.Deadline ?? DateOnly.MaxValue)
                .Take(3);

    public bool HasNoGoals => GoalsWidget is null || GoalsWidget.Goals.Count == 0;

    /// <summary>
    /// Long-term refactor：「投資焦點卡」用的 VM。原本是 Portfolio.Dashboard 內 tab
    /// 的資料來源，現在升到「全域財務儀表板」的總覽 tab，作為對應「投資資產」
    /// 工作頁的 glance summary。Portfolio 頁本身的 Dashboard tab 已移除。
    /// </summary>
    public Assetra.WPF.Features.Portfolio.DashboardViewModel? InvestmentFocusWidget { get; }

    /// <summary>
    /// Phase C：「現金 / 負債焦點卡」共用的資料源 — PortfolioViewModel 已有
    /// TotalCash / TotalLiabilities / CashAccounts / Liabilities 集合。
    /// 不再走 IPortfolioPositionFeed 介面以方便綁定 collection.Count。
    /// </summary>
    public Assetra.WPF.Features.Portfolio.PortfolioViewModel? PortfolioRef { get; }

    /// <summary>
    /// Phase C：「不動產焦點卡」資料源。RealEstateViewModel 內已有
    /// PropertyCount / TotalCurrentValue / TotalMortgageBalance / TotalEquity。
    /// </summary>
    public Assetra.WPF.Features.RealEstate.RealEstateViewModel? RealEstateFocusWidget { get; }

    /// <summary>Phase C 擴展：保險焦點卡（PolicyCount + TotalAnnualPremium）。</summary>
    public Assetra.WPF.Features.Insurance.InsurancePolicyViewModel? InsuranceFocusWidget { get; }

    /// <summary>Phase C 擴展：退休專戶焦點卡（AccountCount + TotalBalance）。</summary>
    public Assetra.WPF.Features.Retirement.RetirementViewModel? RetirementFocusWidget { get; }

    /// <summary>Phase C 擴展：實物資產焦點卡（AssetCount + TotalCurrentValue）。</summary>
    public Assetra.WPF.Features.PhysicalAsset.PhysicalAssetViewModel? PhysicalAssetFocusWidget { get; }

    /// <summary>True 當六個資產類焦點卡至少有一個 VM 注入；全 null 時整個
    /// AssetClassFocusWidget 隱藏，避免空白 header card。</summary>
    public bool HasAnyAssetClassFocus =>
        PortfolioRef is not null
        || RealEstateFocusWidget is not null
        || InsuranceFocusWidget is not null
        || RetirementFocusWidget is not null
        || PhysicalAssetFocusWidget is not null;

    public FinancialOverviewViewModel(
        IFinancialOverviewQueryService queryService,
        IPortfolioPositionFeed portfolio,
        IAppSettingsService? settings = null,
        Assetra.WPF.Features.Goals.GoalsViewModel? goalsWidget = null,
        Assetra.WPF.Features.Fire.FireViewModel? fireWidget = null,
        Assetra.WPF.Features.Assistant.AssistantViewModel? assistantWidget = null,
        Assetra.WPF.Features.Portfolio.DashboardViewModel? investmentFocusWidget = null,
        Assetra.WPF.Features.Portfolio.PortfolioViewModel? portfolioRef = null,
        Assetra.WPF.Features.RealEstate.RealEstateViewModel? realEstateFocusWidget = null,
        Assetra.WPF.Features.Insurance.InsurancePolicyViewModel? insuranceFocusWidget = null,
        Assetra.WPF.Features.Retirement.RetirementViewModel? retirementFocusWidget = null,
        Assetra.WPF.Features.PhysicalAsset.PhysicalAssetViewModel? physicalAssetFocusWidget = null)
    {
        _queryService = queryService ?? throw new ArgumentNullException(nameof(queryService));
        _portfolio = portfolio ?? throw new ArgumentNullException(nameof(portfolio));
        _settings = settings;
        GoalsWidget = goalsWidget;
        FireWidget = fireWidget;
        AssistantWidget = assistantWidget;
        InvestmentFocusWidget = investmentFocusWidget;
        PortfolioRef = portfolioRef;
        RealEstateFocusWidget = realEstateFocusWidget;
        InsuranceFocusWidget = insuranceFocusWidget;
        RetirementFocusWidget = retirementFocusWidget;
        PhysicalAssetFocusWidget = physicalAssetFocusWidget;
        _uiContext = SynchronizationContext.Current;

        // Stock prices arrive asynchronously after Portfolio.LoadAsync() completes.
        // Subscribing to TotalMarketValue lets FinancialOverview re-snapshot once
        // the first batch of live prices lands; without this hook the Investments
        // section freezes at TWD 0 because LoadAsync was called pre-price-fetch.
        _portfolio.PropertyChanged += OnPortfolioPropertyChanged;

        // Stage 2 (Dashboard consolidation)：接收 NavRail 攔截 Trends leaf 後送
        // 過來的 tab 切換請求。lifetime = application；無需 unsubscribe。
        Assetra.WPF.Infrastructure.ShellNavigationEvents.DashboardTabRequested += OnDashboardTabRequested;

        // Stage 2.5 polish：監聽 Goals 集合變化以 refresh TopThreeGoals + HasNoGoals。
        if (GoalsWidget is not null && GoalsWidget.Goals is System.Collections.Specialized.INotifyCollectionChanged ncc)
        {
            ncc.CollectionChanged += (_, _) =>
            {
                OnPropertyChanged(nameof(TopThreeGoals));
                OnPropertyChanged(nameof(HasNoGoals));
            };
        }

        KpiCards = new ReadOnlyObservableCollection<KpiCardVm>(_kpiCards);
        KpiEditorItems = new ReadOnlyObservableCollection<KpiSelectionItemVm>(_kpiEditorItems);
        AssetGroups = new ReadOnlyObservableCollection<AssetGroupVm>(_assetGroups);
        InvestGroups = new ReadOnlyObservableCollection<AssetGroupVm>(_investGroups);
        LiabGroups = new ReadOnlyObservableCollection<AssetGroupVm>(_liabGroups);

        if (_settings is not null)
            _settings.Changed += RebuildKpiCards;

        RebuildKpiCards();
    }

    // Stage 2.5：widget「→ 看全部」連結
    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void NavigateToGoals() =>
        Assetra.WPF.Infrastructure.ShellNavigationEvents.RequestNavigateTo("Goals");

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void NavigateToFire() =>
        Assetra.WPF.Infrastructure.ShellNavigationEvents.RequestNavigateTo("Fire");

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void NavigateToAssistant() =>
        Assetra.WPF.Infrastructure.ShellNavigationEvents.RequestNavigateTo("Assistant");

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void NavigateToPortfolio() =>
        Assetra.WPF.Infrastructure.ShellNavigationEvents.RequestNavigateTo("Portfolio");

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void NavigateToCashAccounts() =>
        Assetra.WPF.Infrastructure.ShellNavigationEvents.RequestNavigateTo("CashAccounts");

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void NavigateToLiabilities() =>
        Assetra.WPF.Infrastructure.ShellNavigationEvents.RequestNavigateTo("Liabilities");

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void NavigateToRealEstate() =>
        Assetra.WPF.Infrastructure.ShellNavigationEvents.RequestNavigateTo("RealEstate");

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void NavigateToInsurance() =>
        Assetra.WPF.Infrastructure.ShellNavigationEvents.RequestNavigateTo("Insurance");

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void NavigateToRetirement() =>
        Assetra.WPF.Infrastructure.ShellNavigationEvents.RequestNavigateTo("Retirement");

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void NavigateToPhysicalAsset() =>
        Assetra.WPF.Infrastructure.ShellNavigationEvents.RequestNavigateTo("PhysicalAsset");

    // ── v2：KPI 編輯 dialog 內 reorder（▲▼）─────────────────────────────────
    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void MoveKpiUp(KpiSelectionItemVm? item)
    {
        if (item is null) return;
        var idx = _kpiEditorItems.IndexOf(item);
        if (idx <= 0) return;
        _kpiEditorItems.Move(idx, idx - 1);
        RecomputeKpiEditorState();
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void MoveKpiDown(KpiSelectionItemVm? item)
    {
        if (item is null) return;
        var idx = _kpiEditorItems.IndexOf(item);
        if (idx < 0 || idx >= _kpiEditorItems.Count - 1) return;
        _kpiEditorItems.Move(idx, idx + 1);
        RecomputeKpiEditorState();
    }

    private void OnDashboardTabRequested(string tabName)
    {
        if (Enum.TryParse<DashboardTab>(tabName, ignoreCase: true, out var tab))
        {
            // 確保 marshal 回 UI thread；event 可能來自任何 caller。
            if (_uiContext is not null && SynchronizationContext.Current != _uiContext)
                _uiContext.Post(_ => SelectedDashboardTab = tab, null);
            else
                SelectedDashboardTab = tab;
        }
    }

    private void OnPortfolioPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // TotalMarketValue / TotalCash 改變時重新載入 — 涵蓋價格更新、現金流、
        // 帳戶 metadata 編輯（PortfolioViewModel 在 ReloadAfterAccountChanged 末端
        // 主動 raise TotalCash PropertyChanged，即使數值未變）。
        if (e.PropertyName == nameof(IPortfolioPositionFeed.TotalMarketValue)
            || e.PropertyName == nameof(IPortfolioPositionFeed.TotalCash))
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
