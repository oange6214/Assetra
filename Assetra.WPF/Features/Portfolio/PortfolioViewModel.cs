using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Assetra.Application.Portfolio.Contracts;
using Assetra.Application.Portfolio.Dtos;
using Assetra.Application.Portfolio.Services;
using Assetra.Core.DomainServices;
using Assetra.Core.Dtos;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Assetra.Infrastructure;
using Assetra.WPF.Features.Portfolio.SubViewModels;
using Assetra.WPF.Infrastructure;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace Assetra.WPF.Features.Portfolio;

public enum PortfolioTab { Dashboard, Positions, AllocationAnalysis, Accounts, Liability, Trades }

/// <summary>Currency option for the edit-asset currency picker.</summary>
public sealed record CurrencyOption(string Code, string Display)
{
    public override string ToString() => Display;
}

public partial class PortfolioViewModel : ObservableObject, IDisposable,
    Contracts.IPortfolioPositionFeed, Contracts.IDashboardNavigation
{
    private readonly IStockSearchService _search;
    private readonly IStockService _stockService;
    private readonly IAppSettingsService? _settingsService;
    private readonly ISnackbarService? _snackbar;
    private readonly IThemeService? _themeService;
    private Action<ApplicationTheme>? _onThemeChanged;
    private Action? _onSettingsChanged;
    // 上次見到的「報價相關」設定值。settings.Changed 可能由非報價變更（含內部記帳）
    // 觸發；唯有這三者實際變動時才主動重抓報價，避免無謂地燒掉 TwelveData 配額並
    // 切斷回饋迴圈。於訂閱接線處從 Current 初始化。
    private string? _lastQuoteProvider;
    private string? _lastFugleApiKey;
    private string? _lastTwelveDataApiKey;
    private readonly ICurrencyService? _currencyService;
    private readonly ICryptoService? _cryptoService;
    // P4.1 — XIRR calculator for asset detail panel KPI matrix. Null when not registered.
    private readonly Assetra.Core.Interfaces.Analysis.IXirrCalculator? _xirrCalculator;
    private readonly IPortfolioLoadService _loadService;
    private readonly ITransactionWorkflowService _transactionWorkflowService;
    private readonly ITradeDeletionWorkflowService _tradeDeletionWorkflowService;
    private readonly PortfolioSellPanelController _sellPanelController = new();
    private readonly PortfolioTradeDialogController _tradeDialogController = new();
    private readonly IPositionDeletionWorkflowService _positionDeletionWorkflowService;
    private readonly ILiabilityMutationWorkflowService _liabilityMutationWorkflowService;
    private readonly IPortfolioSummaryService _summaryService;
    private readonly IPortfolioHistoryMaintenanceService _historyMaintenanceService;

    /// <summary>
    /// Per-position 30 天歷史抓取用（sparkline 欄）。可選；null 時 sparkline 隱藏。
    /// 已透過 CachedStockHistoryProvider 走快取，每符號每天最多打一次外部 API。
    /// </summary>
    private readonly Assetra.Core.Interfaces.IStockHistoryProvider? _stockHistory;

    /// <summary>
    /// Watchlist 用：呼叫 EnsureStockEntryAsync 建立 PortfolioEntry（不建 Trade）。
    /// Null 時 watchlist 命令降級為「no-op + 錯誤訊息」。
    /// </summary>
    private Assetra.Application.Portfolio.Contracts.IAddAssetWorkflowService? _addAssetWorkflow;
    private readonly ILocalizationService? _localization;
    private readonly CompositeDisposable _disposables = new();

    // M6-B mirror cluster — fully encapsulated. Public API exposes
    // ReadOnlyObservableCollection<T>; mutations route through Internal_*
    // helpers. Cascading consumers (DashboardVM / DividendCalendarVM /
    // TradeFilter / SellPanel / AccountDialog) updated to accept the
    // read-only type and cast to INotifyCollectionChanged where they need
    // the .CollectionChanged event.
    private readonly ObservableCollection<PortfolioRowViewModel> _positions = [];
    private readonly ObservableCollection<TradeRowViewModel> _trades = [];
    private readonly ObservableCollection<CashAccountRowViewModel> _cashAccounts = [];
    private readonly ObservableCollection<LiabilityRowViewModel> _liabilities = [];

    public ReadOnlyObservableCollection<PortfolioRowViewModel> Positions { get; }
    public ReadOnlyObservableCollection<TradeRowViewModel> Trades { get; }
    public ReadOnlyObservableCollection<CashAccountRowViewModel> CashAccounts { get; }
    public ReadOnlyObservableCollection<LiabilityRowViewModel> Liabilities { get; }

    /// <summary>
    /// Portfolio-Groups-Refactor P4 — Positions tab chip row 用的群組目錄。
    /// null = 功能未啟用，XAML 隱藏 chip row。
    /// </summary>
    public Assetra.WPF.Features.PortfolioGroups.PortfolioGroupCatalog? GroupCatalog { get; private set; }

    private bool _hasAppliedInitialPositionViewMode;

    /// <summary>XAML 用：是否暴露 group chip row。catalog 存在且至少 1 個 group 時 true。</summary>
    /// <summary>
    /// P3.9 — 排除 system 預設那一個 group。判定改成「user 主動建立過群組」才視為
    /// 有群組功能。Default group (IsSystem=true) 是 schema migration 自動建的、所有
    /// trade/position 預設掛上去，UI 上單獨顯示「預設群組」 chip 跟「全部群組」 等價
    /// 沒功能。改成排除 IsSystem 後 — user 沒主動建群組 → 整條群組 UI 自動消失，
    /// 真有建群組 → UI 出現可用。DB schema / Goals / FIRE 自動追蹤功能全保留。
    /// </summary>
    public bool HasPortfolioGroups => GroupCatalog?.Groups.Any(g => !g.IsSystem) == true;

    IReadOnlyList<PortfolioRowViewModel> Contracts.IPortfolioPositionFeed.Positions => Positions;

    void Contracts.IDashboardNavigation.NavigateTo(PortfolioTab tab) => SelectedTab = tab;

    // ── Internal mutators (used by reload paths + test seeders) ──────────────
    internal void Internal_AddPosition(PortfolioRowViewModel row) => _positions.Add(row);
    internal bool Internal_RemovePosition(PortfolioRowViewModel row) => _positions.Remove(row);
    internal void Internal_RemovePositionAt(int index) => _positions.RemoveAt(index);
    internal void Internal_ClearPositions() => _positions.Clear();
    internal int Internal_PositionsIndexOf(PortfolioRowViewModel row) => _positions.IndexOf(row);
    internal void Internal_AddTrade(TradeRowViewModel row) => _trades.Add(row);
    internal bool Internal_RemoveTrade(TradeRowViewModel row) => _trades.Remove(row);
    internal void Internal_ClearTrades() => _trades.Clear();
    internal void Internal_AddCashAccount(CashAccountRowViewModel row) => _cashAccounts.Add(row);
    internal bool Internal_RemoveCashAccount(CashAccountRowViewModel row) => _cashAccounts.Remove(row);
    internal void Internal_RemoveCashAccountAt(int index) => _cashAccounts.RemoveAt(index);
    internal void Internal_ClearCashAccounts() => _cashAccounts.Clear();
    internal void Internal_AddLiability(LiabilityRowViewModel row) => _liabilities.Add(row);
    internal void Internal_ClearLiabilities() => _liabilities.Clear();

    // Tab-specific view models are built by the shell and attached once all
    // singleton VMs exist, avoiding circular construction inside PortfolioViewModel.
    public DashboardViewModel? Dashboard { get; private set; }
    public Controls.AllocationViewModel? AllocationAnalysis { get; private set; }

    /// <summary>
    /// Task 1.3 — Google-style portfolio tab strip.
    /// Constructed in the ctor after GroupCatalog is assigned.
    /// Tab selection drives <see cref="PortfolioGroupFilter"/> via a PropertyChanged subscription.
    /// </summary>
    public PortfolioTabsViewModel PortfolioTabs { get; }

    // Asset allocation (pie chart) — owned by AllocationPanelViewModel.
    public AllocationPanelViewModel Allocation { get; }
    [ObservableProperty] private bool _isAllocationVisible = true;
    [ObservableProperty] private bool _isMetricCardsExpanded = true;
    [ObservableProperty] private bool _isPositionsSummaryExpanded = true;
    [ObservableProperty] private bool _isCashSummaryExpanded = true;
    [ObservableProperty] private bool _isLiabilitySummaryExpanded = true;

    // Totals
    [ObservableProperty] private decimal _totalCost;
    [ObservableProperty] private decimal _totalMarketValue;
    [ObservableProperty] private decimal _totalPnl;
    [ObservableProperty] private decimal _totalPnlPercent;
    [ObservableProperty] private bool _isTotalPositive;
    [ObservableProperty] private bool _hasNoPositions = true;

    // 現金 / 負債 / 總資產 / 淨資產
    [ObservableProperty] private decimal _totalCash;
    [ObservableProperty] private decimal _totalLiabilities;
    [ObservableProperty] private decimal _totalAssets;
    [ObservableProperty] private decimal _netWorth;
    [ObservableProperty] private bool _hasNoCashAccounts = true;
    [ObservableProperty] private bool _hasNoLiabilities = true;

    public bool ShowQuoteProviderNotice =>
        _settingsService is not null &&
        string.IsNullOrWhiteSpace(_settingsService.Current.TwelveDataApiKey) &&
        Positions.Any(IsUsListedPosition);

    private static bool IsUsListedPosition(PortfolioRowViewModel row) =>
        string.Equals(
            StockExchangeRegistry.TryGet(row.Exchange)?.Country,
            "US",
            StringComparison.OrdinalIgnoreCase);

    // 本日盈虧（基於 PrevClose，報價載入後才有效）
    [ObservableProperty] private decimal _dayPnl;
    [ObservableProperty] private bool _isDayPnlPositive;
    [ObservableProperty] private bool _hasDayPnl;

    private decimal _dayPnlPercent;
    /// <summary>本日盈虧百分比，格式如 (+1.23%) 或 (-0.45%)，直接綁定到 TextBlock.Text。</summary>
    public string DayPnlPercentDisplay =>
        _dayPnlPercent >= 0
            ? $"(+{_dayPnlPercent:F2}%)"
            : $"({_dayPnlPercent:F2}%)";

    // 實現損益統計（所有已平倉交易）
    [ObservableProperty] private decimal _totalRealizedPnl;
    [ObservableProperty] private bool _isTotalRealizedPositive;
    [ObservableProperty] private bool _hasRealizedPnl;

    // 收入 & 股利統計
    [ObservableProperty] private decimal _totalIncome;
    [ObservableProperty] private decimal _totalDividends;
    [ObservableProperty] private bool _hasIncome;
    [ObservableProperty] private bool _hasDividends;

    // Confirm dialog
    [ObservableProperty] private bool _isConfirmDialogOpen;
    [ObservableProperty] private string _confirmDialogMessage = string.Empty;
    private Func<Task>? _confirmDialogAction;

    [RelayCommand]
    private async Task ConfirmDialogYes()
    {
        IsConfirmDialogOpen = false;
        if (_confirmDialogAction is not null)
            await _confirmDialogAction();
        _confirmDialogAction = null;
    }

    [RelayCommand]
    private void ConfirmDialogNo()
    {
        IsConfirmDialogOpen = false;
        _confirmDialogAction = null;
    }

    private void AskConfirm(string message, Func<Task> action)
    {
        ConfirmDialogMessage = message;
        _confirmDialogAction = action;
        IsConfirmDialogOpen = true;
    }

    [ObservableProperty] private bool _hasNoTrades = true;
    [ObservableProperty] private bool _hasAnyDividendTrades;

    /// <summary>Empty-state notice shown above the tabs. Owns its own visibility,
    /// title/message/action text and the action command. Refreshed by
    /// <see cref="RaiseSetupNoticeChanged"/> whenever account/trade counts shift.</summary>
    public SetupNoticeViewModel SetupNotice { get; }

    /// <summary>
    /// Trade filter, pagination, and sort state. All Trades-tab bindings that
    /// previously pointed directly to <see cref="PortfolioViewModel"/> properties
    /// now bind through <c>TradeFilter.*</c>.
    /// </summary>
    public TradeFilterViewModel TradeFilter { get; }

    // Tab state — XAML binds TabControl.SelectedValue (with SelectedValuePath="Tag") to this.
    [ObservableProperty] private PortfolioTab _selectedTab = PortfolioTab.Positions;

    // 負債健康度 + 緊急預備金 — owned by FinancialSummaryViewModel.
    public FinancialSummaryViewModel Financial { get; }

    private void OnMonthlyExpenseFromSubVm(decimal value)
    {
        Financial.Apply(_summaryService.Calculate(BuildSummaryInput()));
        AsyncHelpers.SafeFireAndForget(SaveMonthlyExpenseAsync, "Portfolio.SaveMonthlyExpense");
    }

    private async Task SaveMonthlyExpenseAsync()
    {
        if (_settingsService is null)
            return;
        try
        {
            var updated = _settingsService.Current with { MonthlyExpense = Financial.MonthlyExpense };
            await _settingsService.SaveAsync(updated);
        }
        catch (Exception ex)
        {
            // 儲存偏好設定失敗不影響主要功能
            Log.Warning(ex, "Failed to save monthly expense setting");
        }
    }

    // P2.6 — 在 ConfirmTx 成功時被 TransactionDialogVM 透過 deps callback 觸發。
    // 維護 AppSettings.RecentlyUsedAssetIds 的 LRU 清單：剛用的塞到 [0]、去重、
    // 上限 AppSettings.MaxRecentlyUsedAssets。失敗不影響交易主流程。
    private async Task RecordRecentAssetAsync(Guid id)
    {
        if (_settingsService is null || id == Guid.Empty)
            return;
        try
        {
            var current = _settingsService.Current.RecentlyUsedAssetIds ?? new List<Guid>();
            var updated = new List<Guid>(AppSettings.MaxRecentlyUsedAssets) { id };
            foreach (var existing in current)
            {
                if (existing == id || existing == Guid.Empty)
                    continue;
                updated.Add(existing);
                if (updated.Count >= AppSettings.MaxRecentlyUsedAssets)
                    break;
            }
            var settings = _settingsService.Current with { RecentlyUsedAssetIds = updated };
            await _settingsService.SaveAsync(settings);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to record recently used asset {AssetId}", id);
        }
    }

    [RelayCommand]
    private void RemoveTrade(TradeRowViewModel row)
    {
        var msg = L("Portfolio.Confirm.DeleteTrade", "確定刪除此交易紀錄？");
        AskConfirm(msg, async () =>
        {
            try
            {
                var result = await _tradeDeletionWorkflowService.DeleteAsync(ToTradeDeletionRequest(row));
                if (!result.Success && result.BlockedBySell)
                {
                    _snackbar?.Warning(L("Portfolio.Trade.DeleteBlockedBySell",
                        "請先刪除此股票的賣出記錄，再刪除此買入記錄。"));
                    return;
                }
                _trades.Remove(row);
                HasNoTrades = Trades.Count == 0;
                HasAnyDividendTrades = Trades.Any(t => t.IsCashDividend);
                RebuildRealizedPnl();
                TradeFilter.RefreshTradesView();
                NotifyTradeDependentDetailPropertiesChanged();
                await ReloadAccountBalancesAndSelectedLiabilityScheduleAsync();
                RebuildTotals();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "RemoveTrade failed for trade {TradeId}", row.Id);
                _snackbar?.Error(L("Portfolio.Trade.DeleteFailed", "刪除交易記錄失敗，請稍後再試"));
            }
        });
    }


    /// <summary>Child ViewModel for portfolio value history chart.</summary>
    public PortfolioHistoryViewModel History { get; }

    /// <summary>
    /// Sub-VM that owns all add-new-asset dialog state and commands.
    /// XAML bindings chain through <c>AddAssetDialog.PropertyName</c>.
    /// </summary>
    public AddAssetDialogViewModel AddAssetDialog { get; }

    /// <summary>
    /// Sub-VM that owns all sell-panel state (selling row, price input, fee breakdown,
    /// cash account) and the CancelSell / ConfirmSell commands.
    /// XAML bindings chain through <c>SellPanel.PropertyName</c>.
    /// </summary>
    public SellPanelViewModel SellPanel { get; }

    /// <summary>
    /// Sub-VM that owns all transaction-dialog state and commands.
    /// XAML bindings chain through <c>Transaction.PropertyName</c>.
    /// </summary>
    public SubViewModels.TransactionDialogViewModel Transaction { get; }

    /// <summary>
    /// Sub-VM that owns the edit-asset dialog state and account-management commands
    /// (archive, delete, default-cash toggle, show-archived toggle).
    /// XAML bindings chain through <c>Account.PropertyName</c>.
    /// </summary>
    public SubViewModels.AccountDialogViewModel Account { get; }

    /// <summary>
    /// Sub-VM that owns the loan-schedule confirm-payment command and
    /// amortization-schedule loading.
    /// XAML bindings chain through <c>Loan.PropertyName</c>.
    /// </summary>
    public SubViewModels.LoanDialogViewModel Loan { get; }

    /// <summary>
    /// Edit-existing-liability dialog (Loan / CreditCard fields). Bound by
    /// MainWindow's overlay; opened from the Liability detail panel's
    /// Edit button via <see cref="OpenEditLiabilityCommand"/>.
    /// </summary>
    public SubViewModels.EditLiabilityDialogViewModel EditLiabilityDialog { get; }

    // Dividend calendar
    public DividendCalendarViewModel DivCalendar { get; }

    public PortfolioViewModel(
        PortfolioServices services,
        PortfolioUiServices ui)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(ui);

        // M6-B — wrap collections before any sub-VM gets a reference.
        Positions = new ReadOnlyObservableCollection<PortfolioRowViewModel>(_positions);
        Trades = new ReadOnlyObservableCollection<TradeRowViewModel>(_trades);
        CashAccounts = new ReadOnlyObservableCollection<CashAccountRowViewModel>(_cashAccounts);
        Liabilities = new ReadOnlyObservableCollection<LiabilityRowViewModel>(_liabilities);

        _search = services.Search;
        _snackbar = ui.Snackbar;
        _settingsService = ui.Settings;
        _currencyService = services.Currency;
        // Portfolio-Groups-Refactor P4 — Positions tab chip row 用。null = 功能未啟用。
        GroupCatalog = services.GroupCatalog;
        if (GroupCatalog?.Groups is INotifyCollectionChanged groupChanges)
        {
            Observable
                .FromEventPattern<NotifyCollectionChangedEventHandler, NotifyCollectionChangedEventArgs>(
                    handler => groupChanges.CollectionChanged += handler,
                    handler => groupChanges.CollectionChanged -= handler)
                .ObserveOn(ui.Scheduler)
                .Subscribe(_ =>
                {
                    RefreshPortfolioGroupFilterChips();
                    OnPropertyChanged(nameof(HasPortfolioGroups));
                })
                .DisposeWith(_disposables);
        }
        // Task 1.3 — construct the Google-style tab strip from the current catalog groups.
        // _localization is assigned later in this ctor, but L() is null-safe (returns fallback when null),
        // so the labels are correct immediately. Catalog groups are empty until LoadAsync(); Sync() in
        // RefreshPortfolioGroupFilterChips() re-populates the tabs once the catalog is loaded.
        PortfolioTabs = new PortfolioTabsViewModel(
            GroupCatalog?.Groups ?? Enumerable.Empty<PortfolioGroup>(),
            allLabel: L("Common.All", "全部"),
            ungroupedLabel: L("Portfolio.Group.Ungrouped", "未指定組合"));

        // Task 1.3 — selection → filter: propagate tab changes to the existing PortfolioGroupFilter.
        // OnPortfolioGroupFilterChanged already calls PositionsView.Refresh() + summaries + stats.
        // Task 1.4 — also recompute per-portfolio header aggregates (market value / pnl / trend).
        // TODO (Task 1.5): also refresh stock-vs-ETF focus card from this handler.
        PortfolioTabs.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PortfolioTabsViewModel.SelectedGroupId))
            {
                PortfolioGroupFilter = PortfolioTabs.SelectedGroupId;
                RecomputeSelectedPortfolioHeader();
            }
        };

        // P4.1 — Asset detail KPI 矩陣 XIRR 計算。null = XIRR row 顯示「—」。
        _xirrCalculator = services.Xirr;
        _cryptoService = services.Crypto;
        _loadService = services.Load ?? new NullPortfolioLoadService();
        _transactionWorkflowService = services.TransactionWorkflow ?? new NullTransactionWorkflowService();
        _tradeDeletionWorkflowService = services.TradeDeletionWorkflow ?? new NullTradeDeletionWorkflowService();
        _positionDeletionWorkflowService = services.PositionDeletionWorkflow ?? new NullPositionDeletionWorkflowService();
        _liabilityMutationWorkflowService = services.LiabilityMutation ?? new NullLiabilityMutationWorkflowService();
        var positionMetadataWorkflowService = services.PositionMetadata ?? new NullPositionMetadataWorkflowService();
        var loanMutationWorkflowService = services.LoanMutation ?? new NullLoanMutationWorkflowService();
        _summaryService = services.Summary;
        _historyMaintenanceService = services.HistoryMaintenance
            ?? new NullPortfolioHistoryMaintenanceService();
        _stockHistory = services.History;
        _localization = ui.Localization;
        Allocation = new AllocationPanelViewModel(ui.Localization);
        DivCalendar = new DividendCalendarViewModel(Trades);
        Financial = new FinancialSummaryViewModel(
            getTotalAssets: () => TotalAssets,
            getNetWorth: () => NetWorth,
            onMonthlyExpenseChanged: OnMonthlyExpenseFromSubVm);
        SetupNotice = new SetupNoticeViewModel(
            ui.Localization ?? NullLocalizationService.Instance,
            onAddAccount: OpenAddAccountDialog,
            onAddTrade: AddRecord);
        History = new PortfolioHistoryViewModel(
            services.HistoryQuery ?? new NullPortfolioHistoryQueryService(),
            ui.Localization,
            ui.Settings,
            services.Fx,
            services.Drawdown,
            services.Benchmark,
            services.Twr,
            services.Trades,
            services.Volatility,
            services.Sharpe,
            services.Concentration);

        // TradeFilter must be created before LoadAsync so LoadTradesAsync can call
        // TradeFilter.InitTradeTypeFilters() and TradeFilter.RefreshTradesView().
        TradeFilter = new TradeFilterViewModel(() => Trades, ui.Localization ?? NullLocalizationService.Instance);
        TradeFilter.AttachTradesCollection(Trades);

        // Resolve workflow services for Sub-VM construction (not held as VM fields).
        var addAssetWorkflow = services.AddAsset ?? new NullAddAssetWorkflowService();
        _addAssetWorkflow = services.AddAsset; // 留 null 表示真的沒有；watchlist 命令會檢查
        var accountUpsertWorkflow = services.AccountUpsert ?? new NullAccountUpsertWorkflowService();
        var accountMutationWorkflow = services.AccountMutation ?? new NullAccountMutationWorkflowService();
        var creditCardMutationWorkflow = services.CreditCardMutation ?? new NullCreditCardMutationWorkflowService();
        var creditCardTransactionWorkflow = services.CreditCardTransaction ?? new NullCreditCardTransactionWorkflowService();
        var sellWorkflow = services.Sell ?? new NullSellWorkflowService();
        var tradeMetadataWorkflow = services.TradeMetadata ?? new NullTradeMetadataWorkflowService();
        var loanScheduleService = services.LoanSchedule;

        // Build the AddAssetDialog sub-VM. Tx-dialog field delegates are wired below after
        // the Transaction sub-VM is constructed (delegates reference Transaction properties).
        AddAssetDialog = services.AddAssetDialog ?? new AddAssetDialogViewModel(
            addAssetWorkflow,
            accountUpsertWorkflow,
            _transactionWorkflowService,
            creditCardMutationWorkflow,
            creditCardTransactionWorkflow,
            loanMutationWorkflowService);
        AddAssetDialog.AssetAdded += OnAssetAdded;

        // Build the SellPanel sub-VM and wire delegates for Tx-dialog fields it needs.
        SellPanel = services.SellPanel ?? new SellPanelViewModel(
            sellWorkflow,
            _sellPanelController,
            ui.Snackbar,
            ui.Localization)
        {
            // CashAccounts is init-only: set once here to share the parent's collection
            // reference so both VMs see the same live list without a back-reference.
            CashAccounts = CashAccounts,
        };
        SellPanel.SellCompleted += OnSellCompleted;

        // Build the Account sub-VM before Transaction so that GetDefaultCashAccount
        // (passed as a delegate to Transaction) can safely read Account.DefaultCashAccountId.
        Account = services.Account ?? new SubViewModels.AccountDialogViewModel(
            new SubViewModels.AccountDialogDependencies(
                AccountUpsert: accountUpsertWorkflow,
                AccountMutation: accountMutationWorkflow,
                PositionMetadata: positionMetadataWorkflowService,
                GroupCatalog: services.GroupCatalog,
                Snackbar: _snackbar,
                CashAccounts: CashAccounts,
                LoadCashAccountsAsync: LoadCashAccountsAsync,
                ApplyDefaultCashAccountAsync: ApplyDefaultCashAccountAsync,
                AskConfirm: AskConfirm,
                RebuildTotals: RebuildTotals,
                Localize: L));
        Account.AccountChanged += OnAccountChanged;

        // Build the TransactionDialog sub-VM and wire its reload-callback events.
        // Account must be initialized before Transaction (GetDefaultCashAccount delegate reads Account.DefaultCashAccountId)
        Transaction = services.Transaction ?? new SubViewModels.TransactionDialogViewModel(
            new SubViewModels.TransactionDialogDependencies(
                TransactionWorkflow: _transactionWorkflowService,
                TradeDeletion: _tradeDeletionWorkflowService,
                TradeMetadata: tradeMetadataWorkflow,
                LoanMutation: loanMutationWorkflowService,
                CreditCardTransaction: creditCardTransactionWorkflow,
                Search: _search,
                TradeDialogController: _tradeDialogController,
                AccountUpsert: accountUpsertWorkflow,
                Snackbar: _snackbar,
                Trades: Trades,
                Positions: Positions,
                CashAccounts: CashAccounts,
                Liabilities: Liabilities,
                AddAssetDialog: AddAssetDialog,
                SellPanel: SellPanel,
                GetDefaultCashAccount: GetDefaultCashAccount,
                LoadLoanScheduleAsync: row => Loan?.LoadLoanScheduleAsync(row) ?? Task.CompletedTask,
                LoadLiabilitiesAsync: LoadLiabilitiesAsync,
                LoadPositionsAsync: LoadPositionsAsync,
                LoadTradesAsync: LoadTradesAsync,
                ReloadAccountBalancesAsync: ReloadAccountBalancesAsync,
                ReloadAllAsync: ReloadAllAfterEditAsync,
                RebuildTotals: RebuildTotals,
                Localize: L,
                CategoryRepository: services.CategoryRepository,
                AutoCategorizationRuleRepository: services.AutoCategorizationRuleRepository,
                GroupCatalog: services.GroupCatalog,
                GetDefaultCommissionDiscount: () => _settingsService?.Current?.DefaultCommissionDiscount ?? 1.0m,
                GetSupportedCurrencies: () => _currencyService?.SupportedCurrencies ?? new[] { "TWD", "USD" },
                OpenAddNewAsset: () =>
                {
                    // P2.5 — 從新增交易 dialog 的「+ 新增資產」sentinel 觸發 → 關目前
                    // dialog、開「新增投資」flow。使用者建立完後 dialog 重開時新資產會
                    // 出現在 AvailableAssets（cache 在 OpenTxDialog 會 invalidate）。
                    // CommunityToolkit.Mvvm 產生的 Command property 不會是 null，但 nullable
                    // analyzer 看到 lambda capture this.Transaction 在語法順序上還沒初始化所以
                    // 警告。實際上 lambda 在 dialog 開啟後才執行，Transaction 已被賦值。
                    this!.Transaction!.CloseTxDialogCommand!.Execute(null);
                    if (OpenAddWatchlistDialogCommand.CanExecute(null))
                        OpenAddWatchlistDialogCommand.Execute(null);
                },
                GetRecentAssetIds: () =>
                    (IReadOnlyList<Guid>?)_settingsService?.Current?.RecentlyUsedAssetIds
                        ?? Array.Empty<Guid>(),
                RecordRecentAsset: id => _ = RecordRecentAssetAsync(id),
                TransactionFxRateResolver: services.TransactionFxRateResolver));
        Transaction.TransactionCompleted += OnTransactionCompleted;
        Transaction.TradeDeleted += OnTradeDeleted;

        // Build the Loan sub-VM with delegates into parent state.
        Loan = services.Loan ?? new SubViewModels.LoanDialogViewModel(
            new SubViewModels.LoanDialogDependencies(
                LoanSchedule: loanScheduleService,
                GetSelectedLiabilityRow: () => SelectedLiabilityRow,
                OpenLoanRepaymentTrade: (row, entry) => Transaction.OpenTxDialogForLoanSchedule(row, entry)));

        // Edit-liability dialog: built lazily here so it can share the same
        // workflow service + snackbar without an extra DI override.
        EditLiabilityDialog = new SubViewModels.EditLiabilityDialogViewModel(_liabilityMutationWorkflowService, _snackbar);
        EditLiabilityDialog.LiabilityUpdated += OnLiabilityUpdated;

        // Wire the AddAssetDialog and SellPanel delegates that reference Transaction properties
        // now that Transaction is constructed.
        // Single typed adapter replacing the former 7 ad-hoc Func<...> callbacks.
        // See IBuyExecutionContext for rationale.
        AddAssetDialog.BuyContext = new TransactionBuyContext(Transaction);
        AddAssetDialog.GetCashAccounts = () => CashAccounts;
        // Multi-currency default: new account creation defaults to the user's
        // preferred display currency from Settings instead of hard-coded "TWD".
        // (AppSettings field is PreferredCurrency; SettingsViewModel exposes it
        // as PrimaryCurrency, but here we read the model directly.)
        AddAssetDialog.GetDefaultCurrency = () =>
            _settingsService?.Current.PreferredCurrency ?? "TWD";
        SellPanel.GetTxCommissionDiscountValue = () => Transaction.TxCommissionDiscountValue;
        SellPanel.GetTxFee = () => Transaction.TxFee;
        // MultiCurrency-Trade-Refactor P3 — bridge Tx-dialog Sell sub-VM's cross-currency
        // fields into the sell panel so ConfirmSell can pick them up.
        SellPanel.GetSellActualCashAmount = () => Transaction.Sell.ActualCashAmount;
        SellPanel.GetSellFxRate = () => Transaction.Sell.FxRate;
        // Portfolio-Groups-Refactor P3 — bridge selected group from Tx dialog to sell panel.
        SellPanel.GetSelectedPortfolioGroupId = () => Transaction.SelectedPortfolioGroup?.Id;

        // Rebuild chart colours whenever the user switches theme
        if (ui.Theme is not null)
        {
            _onThemeChanged = _ => History.OnThemeChanged();
            ui.Theme.ThemeChanged += _onThemeChanged;
            _themeService = ui.Theme;
        }

        // Refresh all displayed amounts when currency changes
        if (services.Currency is not null)
            services.Currency.CurrencyChanged += OnCurrencyChanged;

        _stockService = services.Stock;

        if (_settingsService is not null)
        {
            var initial = _settingsService.Current;
            _lastQuoteProvider = initial.QuoteProvider;
            _lastFugleApiKey = initial.FugleApiKey;
            _lastTwelveDataApiKey = initial.TwelveDataApiKey;

            _onSettingsChanged = () =>
            {
                OnPropertyChanged(nameof(ShowQuoteProviderNotice));
                RebuildTotals();

                // 報價來源 / API 金鑰等變更時，報價鏈會即時重讀設定，但執行中的串流
                // 要等下一輪（~10s）輪詢才會用新來源重抓。主動觸發一次立即刷新，
                // 讓使用者改完設定就看到價格更新，而不是「以為要重啟」。fire-and-forget
                // 不阻塞 SaveAsync 的 UI 命令；RefreshNowAsync 內部已吞錯。
                //
                // 但只在「報價相關」設定真的變動時才重抓：Changed 也會被無關設定觸發，
                // 無腦重抓會白白消耗 TwelveData 配額；亦是對未來任何 Changed 來源的防禦。
                var current = _settingsService.Current;
                var quoteRelevantChanged =
                    !string.Equals(current.QuoteProvider, _lastQuoteProvider, StringComparison.Ordinal) ||
                    !string.Equals(current.FugleApiKey, _lastFugleApiKey, StringComparison.Ordinal) ||
                    !string.Equals(current.TwelveDataApiKey, _lastTwelveDataApiKey, StringComparison.Ordinal);

                if (quoteRelevantChanged)
                {
                    _lastQuoteProvider = current.QuoteProvider;
                    _lastFugleApiKey = current.FugleApiKey;
                    _lastTwelveDataApiKey = current.TwelveDataApiKey;
                    AsyncHelpers.SafeFireAndForget(
                        () => _stockService.RefreshNowAsync(),
                        "Portfolio.RefreshQuotesOnSettingsChanged");
                }
            };
            _settingsService.Changed += _onSettingsChanged;
        }

        services.Stock.QuoteStream
            .ObserveOn(ui.Scheduler)
            .Subscribe(UpdatePrices)
            .DisposeWith(_disposables);
    }

    public void AttachTabViewModels(
        DashboardViewModel dashboard,
        Controls.AllocationViewModel allocationAnalysis)
    {
        ArgumentNullException.ThrowIfNull(dashboard);
        ArgumentNullException.ThrowIfNull(allocationAnalysis);

        Dashboard = dashboard;
        AllocationAnalysis = allocationAnalysis;
        OnPropertyChanged(nameof(Dashboard));
        OnPropertyChanged(nameof(AllocationAnalysis));
    }

    public PortfolioViewModel(
        PortfolioRepositories repositories,
        PortfolioServices services,
        PortfolioUiServices ui)
        : this(BuildCompatServices(repositories, services), ui)
    {
    }

    private static PortfolioServices BuildCompatServices(
        PortfolioRepositories repositories,
        PortfolioServices services)
    {
        var balanceQuery = services.BalanceQuery
            ?? (repositories.Trade is not null ? new BalanceQueryService(repositories.Trade) : null);
        var positionQuery = services.PositionQuery
            ?? (repositories.Trade is not null ? new PositionQueryService(repositories.Trade) : null);
        var transactionService = repositories.Trade is not null
            ? new TransactionService(repositories.Trade)
            : null;

        return services with
        {
            Load = services.Load ?? (repositories.Trade is not null && balanceQuery is not null
                ? new PortfolioLoadService(
                    repositories.Portfolio,
                    positionQuery ?? new NullPositionQueryService(),
                    repositories.Trade,
                    balanceQuery,
                    repositories.Asset)
                : null),
            HistoryQuery = services.HistoryQuery ?? new PortfolioHistoryQueryService(repositories.Snapshot),
            TradeDeletionWorkflow = services.TradeDeletionWorkflow ?? (repositories.Trade is not null && positionQuery is not null
                ? new TradeDeletionWorkflowService(
                    repositories.Trade,
                    repositories.Portfolio,
                    positionQuery,
                    loanScheduleRepository: repositories.LoanSchedule)
                : null),
            PositionDeletionWorkflow = services.PositionDeletionWorkflow ?? (repositories.Trade is not null
                ? new PositionDeletionWorkflowService(repositories.Trade, repositories.Portfolio)
                : null),
            AddAsset = services.AddAsset ?? (repositories.Trade is not null && transactionService is not null
                ? new AddAssetWorkflowService(
                    services.Search,
                    services.History,
                    repositories.Portfolio,
                    repositories.Log,
                    transactionService)
                : null),
            AccountUpsert = services.AccountUpsert ?? (repositories.Asset is not null
                ? new AccountUpsertWorkflowService(repositories.Asset)
                : null),
            AccountMutation = services.AccountMutation ?? (repositories.Asset is not null && repositories.Trade is not null
                ? new AccountMutationWorkflowService(repositories.Asset, repositories.Trade)
                : null),
            LoanPayment = services.LoanPayment ?? (repositories.Trade is not null && repositories.LoanSchedule is not null
                ? new LoanPaymentWorkflowService(repositories.Trade, repositories.LoanSchedule)
                : null),
            LoanMutation = services.LoanMutation ?? (repositories.Asset is not null && repositories.LoanSchedule is not null && transactionService is not null
                ? new LoanMutationWorkflowService(repositories.Asset, repositories.LoanSchedule, transactionService)
                : null),
            Sell = services.Sell ?? (repositories.Trade is not null && positionQuery is not null
                ? new SellWorkflowService(repositories.Trade, repositories.Portfolio, repositories.Log, positionQuery)
                : null),
            PositionMetadata = services.PositionMetadata ?? new PositionMetadataWorkflowService(repositories.Portfolio),
            TradeMetadata = services.TradeMetadata ?? (repositories.Trade is not null
                ? new TradeMetadataWorkflowService(repositories.Trade)
                : null),
        };
    }

    /// <summary>
    /// 把每個持倉列的 <c>CommissionDiscount</c> 設為該 Symbol 最新一筆 Buy 的折扣，
    /// 作為未來賣出時的估算假設。交易沒存折扣（legacy）或沒有任何 Buy 時維持 1m。
    /// 由 <see cref="LoadTradesAsync"/> 與 <c>AddPosition</c> 完成後呼叫。
    /// </summary>
    private void ApplyLatestTradeDiscounts()
    {
        // 以 Symbol 分組，取最新 Buy 的折扣
        var latestDiscount = Trades
            .Where(t => t.Type == TradeType.Buy && t.CommissionDiscount is > 0)
            .GroupBy(t => t.Symbol, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(t => t.TradeDate).First().CommissionDiscount!.Value,
                StringComparer.OrdinalIgnoreCase);

        foreach (var row in Positions)
        {
            if (latestDiscount.TryGetValue(row.Symbol, out var disc) && row.CommissionDiscount != disc)
            {
                row.CommissionDiscount = disc;
                row.Refresh();
            }
        }
    }

    private async Task LoadPositionsAsync()
    {
        var loaded = await _loadService.LoadAsync();
        ApplyPositions(loaded);
    }

    private void ApplyPositions(PortfolioLoadResult loaded)
    {
        // M5: assert UI thread; collection mutation requires dispatcher access.
        System.Diagnostics.Debug.Assert(IsOnUiThreadOrTestEnvironment(),
            $"{nameof(ApplyPositions)} must be called on the UI thread");
        var entries = loaded.Entries;
        var snapshots = loaded.PositionSnapshots;
        // Build the new desired set of rows keyed by (Symbol, Exchange, AssetType).
        var newRows = new Dictionary<(string Symbol, string Exchange, AssetType AssetType), PortfolioRowViewModel>();

        foreach (var g in entries.GroupBy(e => (e.Symbol, e.Exchange, e.AssetType)))
        {
            var lots = g.ToList();
            var primary = lots[0];
            var groupSource = lots.Any(l => l.IsActive)
                ? lots.Where(l => l.IsActive)
                : lots;
            var groupIds = groupSource
                .Select(l => l.PortfolioGroupId ?? PortfolioGroup.DefaultId)
                .Distinct()
                .ToList();
            var hasGroupConflict = groupIds.Count > 1;
            Guid? rowGroupId = hasGroupConflict
                ? null
                : groupIds.Count == 0 ? PortfolioGroup.DefaultId : groupIds[0];

            // Archive filter: hide when all lots are archived and ShowArchivedPositions is off.
            if (!ShowArchivedPositions && lots.All(l => !l.IsActive))
                continue;

            PortfolioRowViewModel row;
            if (lots.Count == 1)
            {
                snapshots.TryGetValue(primary.Id, out var snap);

                // Hide-empty filter: projection says qty == 0 (position closed out).
                if (HideEmptyPositions && (snap?.Quantity ?? 0m) == 0m)
                    continue;

                row = ToRow(primary, snap);
                row.IsActive = primary.IsActive;
            }
            else
            {
                // Multiple lots: aggregate into one display row.
                var totalQty = lots.Sum(e => snapshots.TryGetValue(e.Id, out var s) ? s.Quantity : 0m);
                var totalCost = lots.Sum(e => snapshots.TryGetValue(e.Id, out var s) ? s.TotalCost : 0m);
                var firstBuyDate = lots
                    .Select(e => snapshots.TryGetValue(e.Id, out var s) ? s.FirstBuyDate : null)
                    .Where(d => d.HasValue)
                    .Select(d => d!.Value)
                    .OrderBy(d => d)
                    .FirstOrDefault();
                var aggregatedSnap = new PositionSnapshot(
                    primary.Id,
                    totalQty,
                    totalCost,
                    totalQty > 0 ? totalCost / totalQty : 0m,
                    lots.Sum(e => snapshots.TryGetValue(e.Id, out var s) ? s.RealizedPnl : 0m),
                    firstBuyDate == default ? null : firstBuyDate);

                if (HideEmptyPositions && totalQty == 0m)
                    continue;

                row = ToRow(primary, aggregatedSnap);
                row.IsActive = lots.Any(l => l.IsActive);
                foreach (var extra in lots.Skip(1))
                    row.AllEntryIds.Add(extra.Id);
            }

            row.PortfolioGroupId = rowGroupId;
            row.HasPortfolioGroupConflict = hasGroupConflict;
            row.PortfolioGroupDisplay = ResolvePositionGroupDisplay(row.PortfolioGroupId, hasGroupConflict);
            newRows[(g.Key.Symbol, g.Key.Exchange, g.Key.AssetType)] = row;
        }

        // Diff against current Positions: remove stale rows (iterate backward to avoid index shift),
        // then add new rows, then update in-place for rows that exist in both sets.
        var existingIndex = new Dictionary<(string Symbol, string Exchange, AssetType AssetType), PortfolioRowViewModel>();
        foreach (var r in Positions)
            existingIndex[(r.Symbol, r.Exchange, r.AssetType)] = r;

        // Remove rows that are no longer in the new set (backward iteration).
        for (var i = Positions.Count - 1; i >= 0; i--)
        {
            var key = (Positions[i].Symbol, Positions[i].Exchange, Positions[i].AssetType);
            if (!newRows.ContainsKey(key))
                _positions.RemoveAt(i);
        }

        // Add new rows; update existing rows in-place.
        foreach (var (key, newRow) in newRows)
        {
            if (existingIndex.TryGetValue(key, out var existing))
            {
                // Update mutable projection fields on the existing instance so the
                // DataGrid preserves scroll position and selection state.
                existing.Quantity = newRow.Quantity;
                existing.BuyPrice = newRow.BuyPrice;
                existing.IsActive = newRow.IsActive;
                existing.PortfolioGroupId = newRow.PortfolioGroupId;
                existing.HasPortfolioGroupConflict = newRow.HasPortfolioGroupConflict;
                existing.PortfolioGroupDisplay = newRow.PortfolioGroupDisplay;
                existing.AllEntryIds.Clear();
                foreach (var id in newRow.AllEntryIds)
                    existing.AllEntryIds.Add(id);
                existing.Refresh();
            }
            else
            {
                _positions.Add(newRow);
            }
        }

        HasNoPositions = Positions.Count == 0;
        OnPropertyChanged(nameof(ShowQuoteProviderNotice));

        // Refresh Lazy-Upsert suggestion list for TX form editable ComboBox (Task 19).
        // Only active entries — one row per (Symbol, Exchange).
        // Transaction sub-VM owns PositionSuggestions — update it here since ApplyPositions
        // is the only caller that has the full entries list.
        if (Transaction is not null)
        {
            Transaction.ReplacePositionSuggestions(
                entries.Where(x => x.IsActive)
                       .GroupBy(x => (x.Symbol, x.Exchange))
                       .Select(g => g.First())
                       .OrderBy(x => x.Symbol)
                       .Select(e => new SubViewModels.TransactionDialogViewModel.PositionSuggestion(
                           e.Id, e.Symbol, e.Exchange, e.DisplayName)));
        }
    }

    private string ResolvePositionGroupDisplay(Guid? groupId, bool hasGroupConflict)
    {
        if (hasGroupConflict)
            return L("Portfolio.Group.NeedsResolution", "群組待整理");

        return GroupCatalog?.FindById(groupId)?.Name
            ?? L("Portfolio.Group.Ungrouped", "未分組");
    }

    private void ApplyInitialPositionViewMode()
    {
        if (_hasAppliedInitialPositionViewMode)
            return;

        _hasAppliedInitialPositionViewMode = true;
        if (HasPortfolioGroups)
            PositionViewMode = InvestmentPositionViewMode.Group;
    }

    private void RaiseSetupNoticeChanged()
        => SetupNotice.Refresh(HasNoCashAccounts, HasNoTrades);

    public async Task LoadAsync()
    {
        // Portfolio-Groups-Refactor P4 — 先把 group catalog 灌好，PositionsTabPanel 的 chip
        // row 才能在初次顯示就有資料。失敗不阻斷 portfolio 載入。
        var groupCatalogLoaded = GroupCatalog is null;
        if (GroupCatalog is not null)
        {
            try
            {
                await GroupCatalog.EnsureLoadedAsync().ConfigureAwait(true);
                RefreshPortfolioGroupFilterChips();
                OnPropertyChanged(nameof(HasPortfolioGroups));
                groupCatalogLoaded = true;
            }
            catch { /* ignored */ }
        }
        if (groupCatalogLoaded)
            ApplyInitialPositionViewMode();

        // Bypass OnMonthlyExpenseChanged (avoids premature save); RebuildTotals() below reads MonthlyExpense.
        Financial.InitializeMonthlyExpense(_settingsService?.Current?.MonthlyExpense ?? 0m);

        var loaded = await _loadService.LoadAsync();

        ApplyPositions(loaded);
        ApplyCashAccounts(loaded);
        ApplyLiabilities(loaded);
        // 在 RebuildTotals 之前載完 loan schedules，讓「月付 / 下次到期」欄
        // 首次顯示就有資料（不必等使用者切到負債頁點某列才 lazy-load）。
        await EagerLoadLoanSchedulesAsync();

        RebuildTotals();

        // Fetch live prices for crypto positions
        await RefreshCryptoPricesAsync();

        await History.LoadAsync();

        // Load trade history
        ApplyTrades(loaded.Trades);

        // Under the single-truth model, balance seeding is obsolete: CashAccount /
        // LiabilityAccount no longer store Balance/OriginalAmount, so there is nothing
        // to reconcile against. Legacy users simply see 0 until they record Deposit /
        // LoanBorrow transactions themselves.

        // Backfill gaps in snapshot history — fire and forget
        AsyncHelpers.SafeFireAndForget(BackfillAndRefreshAsync, "Portfolio.BackfillAndRefresh");
    }

    /// <summary>Test-only hook — lets tests re-populate the Trades collection after
    /// adding rows directly to the fake repo (bypassing the normal UI flow).</summary>
    internal Task LoadTradesAsyncForTest() => LoadTradesAsync();

    private async Task LoadTradesAsync()
    {
        var loaded = await _loadService.LoadAsync();
        ApplyTrades(loaded.Trades);
    }

    /// <summary>
    /// L1 perf: post-edit cleanup needs positions + trades + cash + liabilities
    /// reapplied in lock-step. Calling each per-slice delegate fires three
    /// separate _loadService.LoadAsync round-trips. This method does one load
    /// and applies every slice from the same snapshot — saves two full reloads
    /// on the edit-with-revision path. Wired into TransactionDialogDeps as the
    /// optional ReloadAllAsync delegate.
    /// </summary>
    private async Task ReloadAllAfterEditAsync()
    {
        var loaded = await _loadService.LoadAsync();
        ApplyPositions(loaded);
        ApplyTrades(loaded.Trades);
        ApplyCashAccounts(loaded);
        ApplyLiabilities(loaded);
    }

    private void ApplyTrades(IReadOnlyList<Trade> trades)
    {
        // M5: assert UI thread; collection mutation requires dispatcher access.
        System.Diagnostics.Debug.Assert(IsOnUiThreadOrTestEnvironment(),
            $"{nameof(ApplyTrades)} must be called on the UI thread");
        // 首次呼叫時建立 type filter 項目（需要 Application resources 已就緒）
        TradeFilter.InitTradeTypeFilters();
        try
        {
            _trades.Clear();
            foreach (var t in trades)
                _trades.Add(new TradeRowViewModel(t));
            HasNoTrades = Trades.Count == 0;
            RaiseSetupNoticeChanged();
            HasAnyDividendTrades = Trades.Any(t => t.IsCashDividend);

            // Rebuild asset filter items — 依交易類型將 symbol 歸到
            // 投資（Buy/Sell/股利）/ 現金（Deposit/Withdrawal/Income/Interest）/ 負債（Loan*）
            TradeFilter.RebuildTradeAssetFilters();

            RebuildRealizedPnl();   // also calls TradeFilter.TradesView?.Refresh()
            TradeFilter.RefreshTradesView();

            // 以最新一筆 Buy 的折扣套到對應持倉，讓預估賣出費用反映當前券商折扣
            ApplyLatestTradeDiscounts();
            NotifyTradeDependentDetailPropertiesChanged();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[Portfolio] LoadTradesAsync failed");
        }
    }

    private void RebuildRealizedPnl()
    {
        var all = Trades.ToList();
        var sellTrades = all.Where(t => t.IsSell && t.RealizedPnl.HasValue).ToList();
        HasRealizedPnl = sellTrades.Count > 0;
        TotalRealizedPnl = sellTrades.Sum(t => t.RealizedPnl!.Value);
        IsTotalRealizedPositive = TotalRealizedPnl >= 0;

        TotalIncome = all.Where(t => t.IsIncome).Sum(t => t.CashAmount ?? 0);
        TotalDividends = all.Where(t => t.IsCashDividend).Sum(t => t.CashAmount ?? 0);
        HasIncome = TotalIncome > 0;
        HasDividends = TotalDividends > 0;

        TradeFilter.TradesView?.Refresh();
    }


    private void RebuildTotals()
    {
        ApplyPositionBaseValuations();

        var summary = _summaryService.Calculate(BuildSummaryInput());

        TotalCost = summary.TotalCost;
        TotalMarketValue = summary.TotalMarketValue;
        TotalPnl = summary.TotalPnl;
        TotalPnlPercent = summary.TotalPnlPercent;
        IsTotalPositive = summary.IsTotalPositive;

        var weights = summary.PositionWeights.ToDictionary(w => w.PositionId, w => w.Percent);
        foreach (var p in Positions)
            p.PercentOfPortfolio = weights.TryGetValue(p.Id, out var percent) ? percent : 0m;

        TotalCash = summary.TotalCash;
        // 每張帳戶卡 / 列上的「佔比」百分比 — 同 PercentOfPortfolio 的做法回寫到 row VM。
        if (TotalCash > 0m)
        {
            foreach (var c in CashAccounts)
                c.PercentOfTotal = c.Balance / TotalCash * 100m;
        }
        else
        {
            foreach (var c in CashAccounts)
                c.PercentOfTotal = 0m;
        }
        TotalLiabilities = summary.TotalLiabilities;
        TotalAssets = summary.TotalAssets;
        NetWorth = summary.NetWorth;
        HasDayPnl = summary.HasDayPnl;
        DayPnl = summary.DayPnl;
        _dayPnlPercent = summary.DayPnlPercent;
        OnPropertyChanged(nameof(DayPnlPercentDisplay));
        IsDayPnlPositive = summary.IsDayPnlPositive;

        // Recompute cached financial summary properties and notify all dependents
        Financial.Apply(summary);

        Allocation.Apply(summary.AllocationSlices);

        // KPI 展開面板用的 per-position 圓餅 — 每次 totals 重算同步刷新
        RebuildPositionPieCharts();

        // Sparkline — 每次 totals 重算順便 queue（去重；走 cache 多數 instant）
        QueueSparklineLoadIfNeeded();

        // Footer 統計：positions / cash / liability 都依 collection view 即時加總，
        // 但 collection 內容變了不自動 raise PropertyChanged，這裡統一推一次。
        RefreshPositionViewGroupSummaries();

        // Task 1.4 — recompute selected-portfolio detail header (value / pnl / trend).
        RecomputeSelectedPortfolioHeader();
        RefreshSelectedPortfolioGroupDetail();
        RaisePositionsFilterStatsChanged();
        RaiseCashFilterStatsChanged();
        RaiseLiabilityFilterStatsChanged();

        // Fire-and-forget: record today's snapshot once prices are live
        AsyncHelpers.SafeFireAndForget(RecordSnapshotAsync, "Portfolio.RecordSnapshot");
    }

    private void ApplyPositionBaseValuations()
    {
        var baseCurrency = ResolveBaseCurrency();
        var rates = _currencyService?.ExchangeRates;
        foreach (var p in Positions)
            p.ApplyBaseValuation(baseCurrency, rates);
    }

    // Position log helpers — seeding helpers removed in Wave 9.3: the trade log is now
    // the single source of truth. SeedPositionLogAsync, SeedBuyTradesAsync, and
    // MigrateTradePortfolioLinksAsync have been deleted.

    private PortfolioRowViewModel ToRow(PortfolioEntry e, PositionSnapshot? snap)
    {
        var isStock = e.AssetType == AssetType.Stock;
        var qty = snap?.Quantity ?? 0m;
        var avgCost = snap?.AverageCost ?? 0m;
        var buyDate = snap?.FirstBuyDate ?? DateOnly.FromDateTime(DateTime.Today);
        var row = new PortfolioRowViewModel
        {
            Id = e.Id,
            Symbol = e.Symbol,
            Exchange = e.Exchange,
            BuyDate = buyDate,
            AssetType = e.AssetType,
            // Sell-side fee estimation (net P&L) — only meaningful for Taiwan stocks/ETFs.
            // CommissionDiscount 建立時先給 1m；LoadTradesAsync 完成後由 ApplyLatestTradeDiscounts
            // 依據該 Symbol 最新一筆 Buy 的 Trade.CommissionDiscount 補回。
            IsEtf = isStock && _search.IsEtf(e.Symbol),
            IsBondEtf = isStock && _search.IsBondEtf(e.Symbol),
            HasProjection = snap is not null,
            Quantity = qty,
            BuyPrice = avgCost,
            Cost = avgCost * qty,
            Name = isStock
                ? (!string.IsNullOrEmpty(e.DisplayName) ? e.DisplayName : (_search.GetName(e.Symbol) ?? string.Empty))
                : e.Symbol,
            Currency = e.Currency,
            IsLoadingPrice = isStock,
            // Non-stock: start with purchase cost as current value (no live feed)
            CurrentPrice = isStock ? 0 : avgCost,
        };
        row.Refresh();   // compute initial Cost, MarketValue, Pnl for all asset types
        row.AllEntryIds.Add(e.Id);
        return row;
    }

    private PortfolioSummaryInput BuildSummaryInput()
    {
        var baseCurrency = ResolveBaseCurrency();
        var rates = _currencyService?.ExchangeRates;

        return new PortfolioSummaryInput(
            Positions.Select(p => new PositionSummaryInput(
                p.Id,
                p.AssetType,
                p.Quantity,
                ConvertToBase(p.Cost, p.Currency, baseCurrency, rates),
                ConvertToBase(p.MarketValue, p.Currency, baseCurrency, rates),
                ConvertToBase(p.NetValue, p.Currency, baseCurrency, rates),
                ConvertToBase(p.CurrentPrice, p.Currency, baseCurrency, rates),
                ConvertToBase(p.PrevClose, p.Currency, baseCurrency, rates),
                p.IsLoadingPrice,
                NativeCurrency: p.Currency,
                BaseCurrency: baseCurrency,
                NativeCost: p.Cost,
                NativeMarketValue: p.MarketValue,
                NativeNetValue: p.NetValue,
                NativeCurrentPrice: p.CurrentPrice,
                NativePrevClose: p.PrevClose)).ToList(),
            CashAccounts.Select(c => new CashBalanceInput(
                c.Id,
                ConvertToBase(c.Balance, c.Currency, baseCurrency, rates),
                NativeCurrency: c.Currency,
                BaseCurrency: baseCurrency,
                NativeBalance: c.Balance)).ToList(),
            Liabilities.Select(l => new LiabilityBalanceInput(
                l.AssetId ?? Guid.Empty,
                ConvertToBase(l.BalanceAsMoney.Amount, l.BalanceAsMoney.Currency, baseCurrency, rates),
                ConvertToBase(l.OriginalAmountAsMoney.Amount, l.OriginalAmountAsMoney.Currency, baseCurrency, rates),
                NativeCurrency: l.BalanceAsMoney.Currency,
                BaseCurrency: baseCurrency,
                NativeBalance: l.BalanceAsMoney.Amount,
                NativeOriginalAmount: l.OriginalAmountAsMoney.Amount)).ToList(),
            Financial.MonthlyExpense,
            baseCurrency);
    }

    private string ResolveBaseCurrency()
    {
        var baseCurrency = _settingsService?.Current.BaseCurrency;
        return string.IsNullOrWhiteSpace(baseCurrency) ? "TWD" : baseCurrency;
    }

    private static decimal ConvertToBase(
        decimal amount,
        string? fromCurrency,
        string baseCurrency,
        IReadOnlyDictionary<string, decimal>? rates) =>
        CurrencyValuation.ConvertToBase(amount, fromCurrency, baseCurrency, rates);

    /// <summary>
    /// Writes <paramref name="id"/> to <see cref="SubViewModels.AccountDialogViewModel.DefaultCashAccountId"/>
    /// and syncs every cash-account row's <c>IsDefault</c> badge.
    /// Also persists to <see cref="IAppSettingsService"/>.
    /// Exposed as a delegate passed into <see cref="SubViewModels.AccountDialogViewModel"/>
    /// so it can call back into the parent's owned state without a circular reference.
    /// </summary>
    private async Task ApplyDefaultCashAccountAsync(Guid? id)
    {
        Account.DefaultCashAccountId = id;
        foreach (var r in CashAccounts)
            r.IsDefault = id.HasValue && r.Id == id.Value;

        if (_settingsService is null)
            return;
        try
        {
            var updated = _settingsService.Current with { DefaultCashAccountId = id };
            await _settingsService.SaveAsync(updated);
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "[Portfolio] Failed to persist DefaultCashAccountId");
        }
    }

    /// <summary>取得預設現金帳戶對應的 Row；無設定或帳戶已刪除時回傳 null。</summary>
    public CashAccountRowViewModel? GetDefaultCashAccount() =>
        Account.DefaultCashAccountId is { } id
            ? CashAccounts.FirstOrDefault(r => r.Id == id)
            : null;

    /// <summary>
    /// Convenience wrapper: looks up a localised string via <see cref="_localization"/> when
    /// available, otherwise falls back to <paramref name="fallback"/>.
    /// </summary>
    private string L(string key, string fallback = "") =>
        _localization?.Get(key, fallback) ?? fallback;

    private static TradeDeletionRequest ToTradeDeletionRequest(TradeRowViewModel row) =>
        new(row.Id, row.Type, row.Symbol, row.Quantity, row.PortfolioEntryId);

    // ── Sell-trigger commands ─────────────────────────────────────────────────────────

    [RelayCommand]
    private void BeginSell(PortfolioRowViewModel row)
    {
        Transaction.OpenTxDialogForPosition(row, "sell");
    }

    /// <summary>側面板「買入」快速動作 — 打開 Tx 對話框，預填當前股票代號。</summary>
    [RelayCommand]
    private void BeginBuyForSelectedPosition()
    {
        if (SelectedPositionRow is null)
            return;
        Transaction.OpenTxDialogForPosition(SelectedPositionRow, "buy");
    }

    /// <summary>側面板「配息入帳」快速動作 — 打開 Tx 對話框並預選此持倉。</summary>
    [RelayCommand]
    private void BeginDividendForSelectedPosition()
    {
        if (SelectedPositionRow is null)
            return;
        Transaction.OpenTxDialogForPosition(SelectedPositionRow, "cashDiv");
    }

    /// <summary>側面板「賣出」快速動作 — 呼叫既有 BeginSell，但以 SelectedPositionRow 為目標。</summary>
    [RelayCommand]
    private void BeginSellForSelectedPosition()
    {
        if (SelectedPositionRow is null)
            return;
        BeginSell(SelectedPositionRow);
    }

    // 全域「新增紀錄」按鈕 — 一律開啟紀錄對話框
    [RelayCommand]
    private void AddRecord() => Transaction.OpenTxDialog();

    /// <summary>
    /// P4.9e — Cash detail panel「+ 新增交易」ContextMenu item 用：開啟 Tx 對話框並
    /// 預先設定 TxType + 預填現金帳戶為當前選定列。`txType` 接受
    /// "income" / "deposit" / "withdrawal" / "transfer"。
    /// </summary>
    [RelayCommand]
    private void BeginTxForSelectedCash(string txType)
    {
        if (SelectedCashRow is null || string.IsNullOrEmpty(txType))
            return;
        Transaction.OpenTxDialogForCashAccount(SelectedCashRow, txType);
    }

    /// <summary>
    /// P4.9e — Liability detail panel「+ 新增交易」ContextMenu item 用。
    /// Loan 走 `loanBorrow` / `loanRepay`（預填 Loan.Label），CreditCard 走
    /// `creditCardCharge` / `creditCardPayment`（無 Loan.Label，由 dialog 內部
    /// 帶 LiabilityAssetId）。
    /// </summary>
    [RelayCommand]
    private void BeginTxForSelectedLiability(string txType)
    {
        if (SelectedLiabilityRow is null || string.IsNullOrEmpty(txType))
            return;
        Transaction.OpenTxDialogForLiability(SelectedLiabilityRow, txType);
    }

    /// <summary>開啟新增現金帳戶對話框（由現金 tab 的「新增帳戶」按鈕呼叫）。</summary>
    [RelayCommand]
    private void OpenAddAccountDialog()
    {
        SelectedTab = PortfolioTab.Accounts;
        AddAssetDialog.ResetForAccountForm();
    }

    [RelayCommand]
    private void OpenAddLiabilityDialog()
    {
        SelectedTab = PortfolioTab.Liability;
        AddAssetDialog.ResetForLiabilityForm();
    }

    // ── Supported currencies (static list — referenced from XAML as PortfolioViewModel.SupportedCurrencies) ─

    public static IReadOnlyList<CurrencyOption> SupportedCurrencies =>
        SubViewModels.AccountDialogViewModel.SupportedCurrencies;

    // ── Cash account loading ──────────────────────────────────────────────────────────

    private async Task LoadCashAccountsAsync()
    {
        var loaded = await _loadService.LoadAsync();
        ApplyCashAccounts(loaded);
    }

    private void ApplyCashAccounts(PortfolioLoadResult loaded)
    {
        // M5: assert UI thread; collection mutation requires dispatcher access.
        System.Diagnostics.Debug.Assert(IsOnUiThreadOrTestEnvironment(),
            $"{nameof(ApplyCashAccounts)} must be called on the UI thread");
        var accounts = loaded.CashAccounts;
        var balances = loaded.CashBalances;
        // Diagnostic log: 確認 DB 載入時 Subtype 是否帶回。若全部 row 的 Subtype 都是 null，
        // 表示 DB 沒寫入或 schema/SELECT 出問題（v0.28+ taxonomy refactor 才有 Subtype）。
        foreach (var a in accounts)
            Serilog.Log.Information("[Portfolio.LoadCash] id={Id} name={Name} subtype={Subtype} groupId={GroupId}",
                a.Id, a.Name, a.Subtype ?? "(null)", a.GroupId?.ToString() ?? "(null)");
        var visibleAccounts = accounts
            .Where(a => Account.ShowArchivedAccounts || a.IsActive)
            .ToList();
        var newRows = visibleAccounts.ToDictionary(
            a => a.Id,
            a => new CashAccountRowViewModel(a, balances.TryGetValue(a.Id, out var v) ? v.Amount : 0m));
        var existingIndex = CashAccounts.ToDictionary(r => r.Id);

        for (var i = CashAccounts.Count - 1; i >= 0; i--)
        {
            if (!newRows.ContainsKey(CashAccounts[i].Id))
                _cashAccounts.RemoveAt(i);
        }

        foreach (var account in visibleAccounts)
        {
            var bal = balances.TryGetValue(account.Id, out var v) ? v.Amount : 0m;
            if (existingIndex.TryGetValue(account.Id, out var existing))
            {
                existing.Name = account.Name;
                existing.Currency = account.Currency;
                existing.Balance = bal;
                existing.IsActive = account.IsActive;
                // Subtype 必須在 reload 時同步：使用者改細分類後，AccountChanged event 觸發
                // ReloadAfterAccountChangedAsync → 此處。若不同步，列表上的 chip 只會在
                // app 重啟（走 ctor）時才顯示，當下 session 不會更新。
                existing.Subtype = account.Subtype;
            }
            else
            {
                _cashAccounts.Add(newRows[account.Id]);
            }
        }
        HasNoCashAccounts = CashAccounts.Count == 0;
        RaiseSetupNoticeChanged();

        var savedId = _settingsService?.Current?.DefaultCashAccountId;
        if (savedId.HasValue && CashAccounts.All(r => r.Id != savedId.Value))
            savedId = null;
        Account.DefaultCashAccountId = savedId;
        foreach (var r in CashAccounts)
            r.IsDefault = savedId.HasValue && r.Id == savedId.Value;

        // CashAccountSuggestions now lives on the Transaction sub-VM; update it here
        // since ApplyCashAccounts is the only caller with the full account list.
        if (Transaction is not null)
        {
            Transaction.ReplaceCashAccountSuggestions(
                accounts.Where(a => a.IsActive).OrderBy(a => a.Name).Select(a => a.Name));
        }
    }

    // ── Liability loading ─────────────────────────────────────────────────────────────

    private async Task LoadLiabilitiesAsync()
    {
        var loaded = await _loadService.LoadAsync();
        ApplyLiabilities(loaded);
        await EagerLoadLoanSchedulesAsync();
    }

    /// <summary>
    /// 提早載入所有 loan 的攤還表 — 否則「月付 / 下次到期」欄首次開啟 App 會顯示「—」
    /// 直到使用者點到該列才 lazy-load。對於只有少數負債的個人理財場景，eager load 成本
    /// 可以接受，換來首屏即有完整資訊的體驗。
    /// 從主 LoadAsync 與 LoadLiabilitiesAsync 兩處共用。
    /// </summary>
    private async Task EagerLoadLoanSchedulesAsync()
    {
        if (Loan is null)
            return;
        foreach (var row in Liabilities.Where(l => l.IsLoan && !l.IsScheduleLoaded).ToList())
        {
            try
            { await Loan.LoadLoanScheduleAsync(row); }
            catch { /* 單筆失敗不影響整體；該列維持 — 顯示 */ }
        }
    }

    private void ApplyLiabilities(PortfolioLoadResult loaded)
    {
        // M5: assert UI thread; collection mutation requires dispatcher access.
        System.Diagnostics.Debug.Assert(IsOnUiThreadOrTestEnvironment(),
            $"{nameof(ApplyLiabilities)} must be called on the UI thread");
        var snapshots = loaded.LiabilitySnapshots;
        var liabilityAssets = loaded.LiabilityAssets;

        _liabilities.Clear();
        foreach (var (label, snap) in snapshots.OrderBy(kv => kv.Key))
        {
            liabilityAssets.TryGetValue(label, out var asset);
            _liabilities.Add(new LiabilityRowViewModel(label, snap, asset));
        }

        foreach (var (name, asset) in liabilityAssets)
        {
            if (!snapshots.ContainsKey(name))
                _liabilities.Add(new LiabilityRowViewModel(name, LiabilitySnapshot.Empty, asset));
        }

        HasNoLiabilities = Liabilities.Count == 0;
        Transaction?.NotifyLoanLabelSuggestionsChanged();
    }

    public void Dispose()
    {
        if (_currencyService is not null)
            _currencyService.CurrencyChanged -= OnCurrencyChanged;
        if (_themeService is not null && _onThemeChanged is not null)
            _themeService.ThemeChanged -= _onThemeChanged;
        if (_settingsService is not null && _onSettingsChanged is not null)
            _settingsService.Changed -= _onSettingsChanged;
        AddAssetDialog.AssetAdded -= OnAssetAdded;
        AddAssetDialog.CancelPendingFetch();
        SellPanel.SellCompleted -= OnSellCompleted;
        Transaction.TransactionCompleted -= OnTransactionCompleted;
        Transaction.TradeDeleted -= OnTradeDeleted;
        Account.AccountChanged -= OnAccountChanged;
        TradeFilter.Dispose();
        _disposables.Dispose();
    }

    /// <summary>
    /// True when running on the WPF dispatcher thread of a real application,
    /// OR when no real WPF application is running (xUnit test environment).
    /// The real production app is the <c>Assetra.WPF.App</c> subclass; tests that
    /// instantiate the bare <c>System.Windows.Application</c> base class (e.g.
    /// <c>ControlsBehaviorTests.EnsureResources</c>) get the test-bypass path so
    /// that subsequent tests on different threads don't trip this assertion.
    /// We deliberately avoid touching any <c>DependencyProperty</c> (e.g.
    /// <c>MainWindow</c>) because reading those from off-dispatcher threads
    /// throws.
    /// </summary>
    private static bool IsOnUiThreadOrTestEnvironment()
    {
        var app = System.Windows.Application.Current;
        if (app is null)
            return true;                  // no Application at all
        if (app.GetType() == typeof(System.Windows.Application))
            return true; // bare-base test fake
        return app.Dispatcher.CheckAccess();           // real app subclass — must be on dispatcher
    }
}

