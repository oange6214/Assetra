using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Windows.Data;
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
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using Serilog;
using SkiaSharp;
using Wpf.Ui.Appearance;

namespace Assetra.WPF.Features.Portfolio;

public enum PortfolioTab { Dashboard, Positions, AllocationAnalysis, Accounts, Liability, Trades }

/// <summary>Currency option for the edit-asset currency picker.</summary>
public sealed record CurrencyOption(string Code, string Display)
{
    public override string ToString() => Display;
}

public partial class PortfolioViewModel : ObservableObject, IDisposable
{
    private readonly IPortfolioRepository _repo;
    private readonly IStockSearchService _search;
    private readonly IPortfolioPositionLogRepository _logRepo;
    private readonly ITradeRepository _tradeRepo;
    private readonly IAppSettingsService? _settingsService;
    private readonly ISnackbarService? _snackbar;
    private readonly IThemeService? _themeService;
    private Action<ApplicationTheme>? _onThemeChanged;
    private readonly ICurrencyService? _currencyService;
    private readonly IAssetRepository? _assetRepo;
    private readonly ILoanScheduleRepository? _loanScheduleRepo;
    private readonly ICryptoService? _cryptoService;
    private readonly IStockHistoryProvider? _historyProvider;
    private readonly IBalanceQueryService _balanceQuery;
    private readonly IPositionQueryService _positionQuery;
    private readonly IPortfolioLoadService _loadService;
    private readonly ITransactionWorkflowService _transactionWorkflowService;
    private readonly ITradeDeletionWorkflowService _tradeDeletionWorkflowService;
    private readonly PortfolioSellPanelController _sellPanelController = new();
    private readonly PortfolioTradeDialogController _tradeDialogController = new();
    private readonly IPositionDeletionWorkflowService _positionDeletionWorkflowService;
    private readonly IPortfolioSummaryService _summaryService;
    private readonly IPortfolioHistoryMaintenanceService _historyMaintenanceService;
    private readonly ILocalizationService? _localization;
    private readonly CompositeDisposable _disposables = new();

    public ObservableCollection<PortfolioRowViewModel> Positions { get; } = [];
    public ObservableCollection<TradeRowViewModel> Trades { get; } = [];
    public ObservableCollection<CashAccountRowViewModel> CashAccounts { get; } = [];
    public ObservableCollection<LiabilityRowViewModel> Liabilities { get; } = [];

    // Asset allocation (pie chart)
    public ObservableCollection<AssetAllocationSlice> AllocationSlices { get; } = [];
    [ObservableProperty] private ISeries[] _allocationPieSeries = [];
    [ObservableProperty] private bool _isAllocationVisible = true;
    [ObservableProperty] private bool _isMetricCardsExpanded = true;
    [ObservableProperty] private bool _isPositionsSummaryExpanded = true;
    [ObservableProperty] private bool _isCashSummaryExpanded = true;
    [ObservableProperty] private bool _isLiabilitySummaryExpanded = true;
    [ObservableProperty] private bool _isDivCalendarExpanded = true;

    public bool HasAllocationData => AllocationSlices.Count > 0;

    // Totals
    [ObservableProperty] private decimal _totalCost;
    [ObservableProperty] private decimal _totalMarketValue;
    [ObservableProperty] private decimal _totalPnl;
    [ObservableProperty] private decimal _totalPnlPercent;
    [ObservableProperty] private bool _isTotalPositive;
    [ObservableProperty] private bool _hasNoPositions = true;
    [ObservableProperty] private bool _showWelcomeBanner;

    // 現金 / 負債 / 總資產 / 淨資產
    [ObservableProperty] private decimal _totalCash;
    [ObservableProperty] private decimal _totalLiabilities;
    [ObservableProperty] private decimal _totalAssets;
    [ObservableProperty] private decimal _netWorth;
    [ObservableProperty] private bool _hasNoCashAccounts = true;
    [ObservableProperty] private bool _hasNoLiabilities = true;

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

    [ObservableProperty] private string _filterText = string.Empty;
    [ObservableProperty] private bool _hasNoTrades = true;
    [ObservableProperty] private bool _hasAnyDividendTrades;

    /// <summary>
    /// Trade filter, pagination, and sort state. All Trades-tab bindings that
    /// previously pointed directly to <see cref="PortfolioViewModel"/> properties
    /// now bind through <c>TradeFilter.*</c>.
    /// </summary>
    public TradeFilterViewModel TradeFilter { get; }

    // Tab state
    [ObservableProperty] private PortfolioTab _selectedTab = PortfolioTab.Positions;

    public bool IsDashboardTab
    {
        get => SelectedTab == PortfolioTab.Dashboard;
        set { if (value) SelectedTab = PortfolioTab.Dashboard; }
    }

    public bool IsPositionsTab
    {
        get => SelectedTab == PortfolioTab.Positions;
        set { if (value) SelectedTab = PortfolioTab.Positions; }
    }

    public bool IsAllocationAnalysisTab
    {
        get => SelectedTab == PortfolioTab.AllocationAnalysis;
        set { if (value) SelectedTab = PortfolioTab.AllocationAnalysis; }
    }

    public bool IsAccountsTab
    {
        get => SelectedTab == PortfolioTab.Accounts;
        set { if (value) SelectedTab = PortfolioTab.Accounts; }
    }

    public bool IsLiabilityTab
    {
        get => SelectedTab == PortfolioTab.Liability;
        set { if (value) SelectedTab = PortfolioTab.Liability; }
    }

    public bool IsTradesTab
    {
        get => SelectedTab == PortfolioTab.Trades;
        set { if (value) SelectedTab = PortfolioTab.Trades; }
    }

    partial void OnSelectedTabChanged(PortfolioTab value)
    {
        OnPropertyChanged(nameof(IsDashboardTab));
        OnPropertyChanged(nameof(IsPositionsTab));
        OnPropertyChanged(nameof(IsAllocationAnalysisTab));
        OnPropertyChanged(nameof(IsAccountsTab));
        OnPropertyChanged(nameof(IsLiabilityTab));
        OnPropertyChanged(nameof(IsTradesTab));
    }

    // 負債健康度（供 Liability tab 摘要卡片使用）
    /// <summary>負債 / 總資產，0–100 範圍，供 ProgressBar 直接綁定。Cached via RecalcFinancialSummary().</summary>
    [ObservableProperty] private decimal _debtRatioValue;
    public string DebtRatioDisplay => TotalAssets > 0 ? $"{DebtRatioValue:F1}%" : "—";
    public string LeverageRatioDisplay => NetWorth > 0 ? $"{TotalAssets / NetWorth:F2}" : "—";
    public bool IsDebtHealthy => DebtRatioValue < 30m;
    public bool IsDebtWarning => DebtRatioValue is >= 30m and < 50m;
    public bool IsDebtDanger => DebtRatioValue >= 50m;

    /// <summary>"healthy" | "warning" | "danger" | "none" — single binding for XAML color triggers.</summary>
    public string DebtStatusTag => IsDebtDanger ? "danger" : IsDebtWarning ? "warning" : IsDebtHealthy ? "healthy" : "none";

    /// <summary>所有負債的原始借款總額（OriginalAmount = 0 的條目以 Balance 補位）。Cached via RecalcFinancialSummary().</summary>
    [ObservableProperty] private decimal _totalOriginalLiabilities;

    /// <summary>已繳百分比（0–100），供 ProgressBar 直接綁定。Cached via RecalcFinancialSummary().</summary>
    [ObservableProperty] private decimal _paidPercentValue;

    public string PaidPercentDisplay => $"{PaidPercentValue:F1}%";

    public string TotalOriginalDisplay =>
        $"NT${TotalOriginalLiabilities:N0}";

    // 緊急預備金（供 Cash tab 摘要卡片使用）
    /// <summary>每月預估開銷（從 AppSettings 讀取，可由 UI 修改）。</summary>
    [ObservableProperty] private decimal _monthlyExpense;

    partial void OnMonthlyExpenseChanged(decimal value)
    {
        ApplyFinancialSummary(_summaryService.Calculate(BuildSummaryInput()));
        OnPropertyChanged(nameof(IsMonthlyExpenseSet));
        _ = SaveMonthlyExpenseAsync();
    }

    public bool IsMonthlyExpenseSet => MonthlyExpense > 0;

    /// <summary>可撐幾個月（無上限）。Cached via RecalcFinancialSummary().</summary>
    [ObservableProperty] private decimal _emergencyFundMonths;

    public string EmergencyFundMonthsDisplay =>
        MonthlyExpense > 0 ? $"{EmergencyFundMonths:F1}" : "—";

    /// <summary>0–100，超過 12 個月視為滿格。</summary>
    public decimal EmergencyFundBarValue =>
        MonthlyExpense > 0 ? Math.Min(EmergencyFundMonths / 12m * 100m, 100m) : 0m;

    public bool IsEmergencySafe => EmergencyFundMonths >= 6m;
    public bool IsEmergencyWarning => EmergencyFundMonths is >= 3m and < 6m;
    public bool IsEmergencyDanger => MonthlyExpense > 0 && EmergencyFundMonths < 3m;

    /// <summary>"safe" | "warning" | "danger" | "none" — single binding for XAML color triggers.</summary>
    public string EmergencyStatusTag => IsEmergencyDanger ? "danger" : IsEmergencyWarning ? "warning" : IsEmergencySafe ? "safe" : "none";

    private async Task SaveMonthlyExpenseAsync()
    {
        if (_settingsService is null)
            return;
        try
        {
            var updated = _settingsService.Current with { MonthlyExpense = MonthlyExpense };
            await _settingsService.SaveAsync(updated);
        }
        catch (Exception ex)
        {
            // 儲存偏好設定失敗不影響主要功能
            Log.Warning(ex, "Failed to save monthly expense setting");
        }
    }

    [RelayCommand]
    private async Task DismissWelcomeBannerAsync()
    {
        ShowWelcomeBanner = false;
        if (_settingsService is null)
            return;
        try
        {
            await _settingsService.SaveAsync(_settingsService.Current with { HasShownWelcomeBanner = true });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to dismiss welcome banner");
        }
    }

    private ICollectionView? _positionsView;
    public ICollectionView PositionsView
    {
        get
        {
            if (_positionsView is null)
            {
                _positionsView = CollectionViewSource.GetDefaultView(Positions);
                _positionsView.Filter = FilterPosition;
            }
            return _positionsView;
        }
    }

    partial void OnFilterTextChanged(string value) => PositionsView.Refresh();

    private bool FilterPosition(object obj)
    {
        if (obj is not PortfolioRowViewModel row)
            return false;

        if (string.IsNullOrWhiteSpace(FilterText))
            return true;

        return row.Symbol.Contains(FilterText, StringComparison.OrdinalIgnoreCase)
            || row.Name.Contains(FilterText, StringComparison.OrdinalIgnoreCase);
    }

    // Cash / Liability filters
    [ObservableProperty] private string _cashFilterText = string.Empty;
    [ObservableProperty] private string _liabilityFilterText = string.Empty;

    private ICollectionView? _cashAccountsView;
    public ICollectionView CashAccountsView
    {
        get
        {
            if (_cashAccountsView is null)
            {
                _cashAccountsView = CollectionViewSource.GetDefaultView(CashAccounts);
                _cashAccountsView.Filter = FilterCashAccount;
            }
            return _cashAccountsView;
        }
    }

    partial void OnCashFilterTextChanged(string value) => CashAccountsView.Refresh();

    private bool FilterCashAccount(object obj)
        => obj is CashAccountRowViewModel row
           && (string.IsNullOrEmpty(CashFilterText)
               || row.Name.Contains(CashFilterText, StringComparison.OrdinalIgnoreCase));

    private ICollectionView? _liabilitiesView;
    public ICollectionView LiabilitiesView
    {
        get
        {
            if (_liabilitiesView is null)
            {
                _liabilitiesView = CollectionViewSource.GetDefaultView(Liabilities);
                _liabilitiesView.Filter = FilterLiability;
            }
            return _liabilitiesView;
        }
    }

    partial void OnLiabilityFilterTextChanged(string value) => LiabilitiesView.Refresh();

    private bool FilterLiability(object obj)
        => obj is LiabilityRowViewModel row
           && (string.IsNullOrEmpty(LiabilityFilterText)
               || row.Name.Contains(LiabilityFilterText, StringComparison.OrdinalIgnoreCase));

    /// <summary>Called from DividendCalendarPanel when a month cell is clicked.</summary>
    [RelayCommand]
    private void FilterByDividendMonth(int month)
    {
        TradeFilter.FilterByDividendMonth(month, DivCalendarYear);
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
                Trades.Remove(row);
                HasNoTrades = Trades.Count == 0;
                HasAnyDividendTrades = Trades.Any(t => t.IsCashDividend);
                RebuildRealizedPnl();
                TradeFilter.RefreshTradesView();
                await ReloadAccountBalancesAsync();
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

    // Dividend calendar
    [ObservableProperty] private int _divCalendarYear = DateTime.Today.Year;

    [RelayCommand]
    private void DivCalendarPrevYear() => DivCalendarYear--;

    [RelayCommand]
    private void DivCalendarNextYear() => DivCalendarYear++;

    public IReadOnlyDictionary<int, decimal> GetDividendsByMonth(int year)
    {
        return Trades
            .Where(t => t.IsCashDividend && t.TradeDate.Year == year)
            .GroupBy(t => t.TradeDate.Month)
            .ToDictionary(g => g.Key, g => g.Sum(t => t.CashAmount ?? 0));
    }

    public PortfolioViewModel(
        PortfolioRepositories repositories,
        PortfolioServices services,
        PortfolioUiServices ui)
    {
        ArgumentNullException.ThrowIfNull(repositories);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(ui);

        _repo = repositories.Portfolio;
        _search = services.Search;
        _snackbar = ui.Snackbar;
        _logRepo = repositories.PositionLog;
        _tradeRepo = repositories.Trade ?? new NullTradeRepository();
        _settingsService = ui.Settings;
        _currencyService = services.Currency;
        _assetRepo = repositories.Asset;
        _loanScheduleRepo = repositories.LoanSchedule;
        _cryptoService = services.Crypto;
        _historyProvider = services.History;
        _balanceQuery = services.BalanceQuery ?? new NullBalanceQueryService();
        _positionQuery = services.PositionQuery ?? new NullPositionQueryService();
        _loadService = services.Load ?? new PortfolioLoadService(
            _repo,
            _positionQuery,
            _tradeRepo,
            _balanceQuery,
            _assetRepo);
        // The inline txService backs both the TransactionWorkflow fallback and the LoanMutation
        // fallback so that trade writes are always routed to the same ITradeRepository.
        var inlineTxService = (ITransactionService)new TransactionService(_tradeRepo);
        _transactionWorkflowService = services.TransactionWorkflow
            ?? new TransactionWorkflowService(inlineTxService);
        _tradeDeletionWorkflowService = services.TradeDeletionWorkflow
            ?? new TradeDeletionWorkflowService(_tradeRepo, _repo, _positionQuery);
        _positionDeletionWorkflowService = services.PositionDeletionWorkflow
            ?? new PositionDeletionWorkflowService(_tradeRepo, _repo);
        var positionMetadataWorkflowService = (IPositionMetadataWorkflowService)new PositionMetadataWorkflowService(_repo);
        var loanMutationWorkflowService = _assetRepo is not null && _loanScheduleRepo is not null
            ? (ILoanMutationWorkflowService)new LoanMutationWorkflowService(
                _assetRepo,
                _loanScheduleRepo,
                inlineTxService)
            : new NullLoanMutationWorkflowService();
        _summaryService = services.Summary;
        _historyMaintenanceService = services.HistoryMaintenance
            ?? new NullPortfolioHistoryMaintenanceService();
        _localization = ui.Localization;
        History = new PortfolioHistoryViewModel(
            services.HistoryQuery ?? new PortfolioHistoryQueryService(repositories.Snapshot),
            ui.Localization);

        // TradeFilter must be created before LoadAsync so LoadTradesAsync can call
        // TradeFilter.InitTradeTypeFilters() and TradeFilter.RefreshTradesView().
        TradeFilter = new TradeFilterViewModel(() => Trades, ui.Localization ?? NullLocalizationService.Instance);
        TradeFilter.AttachTradesCollection(Trades);

        // Build inline workflow services needed only for Sub-VM construction (not held as fields).
        var addAssetWorkflow = new AddAssetWorkflowService(
            _search, _historyProvider, _repo, _logRepo, inlineTxService);
        var accountUpsertWorkflow = _assetRepo is not null
            ? (IAccountUpsertWorkflowService)new AccountUpsertWorkflowService(_assetRepo)
            : new NullAccountUpsertWorkflowService();
        var accountMutationWorkflow = _assetRepo is not null
            ? (IAccountMutationWorkflowService)new AccountMutationWorkflowService(_assetRepo)
            : new NullAccountMutationWorkflowService();
        var sellWorkflow = (ISellWorkflowService)new SellWorkflowService(_tradeRepo, _repo, _logRepo, _positionQuery);
        var tradeMetadataWorkflow = (ITradeMetadataWorkflowService)new TradeMetadataWorkflowService(_tradeRepo);
        var loanPaymentWorkflow = _loanScheduleRepo is not null
            ? (ILoanPaymentWorkflowService)new LoanPaymentWorkflowService(_tradeRepo, _loanScheduleRepo)
            : new NullLoanPaymentWorkflowService();

        // Build the AddAssetDialog sub-VM. Tx-dialog field delegates are wired below after
        // the Transaction sub-VM is constructed (delegates reference Transaction properties).
        AddAssetDialog = services.AddAssetDialog ?? new AddAssetDialogViewModel(
            addAssetWorkflow,
            accountUpsertWorkflow);
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
                AssetRepo: _assetRepo,
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
                Search: _search,
                TradeDialogController: _tradeDialogController,
                AssetRepo: _assetRepo,
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
                RebuildTotals: RebuildTotals,
                Localize: L));
        Transaction.TransactionCompleted += OnTransactionCompleted;
        Transaction.TradeDeleted += OnTradeDeleted;

        // Build the Loan sub-VM with delegates into parent state.
        Loan = services.Loan ?? new SubViewModels.LoanDialogViewModel(
            new SubViewModels.LoanDialogDependencies(
                LoanPayment: loanPaymentWorkflow,
                LoanScheduleRepo: _loanScheduleRepo,
                GetSelectedLiabilityRow: () => SelectedLiabilityRow,
                GetTxCashAccountId: () => Transaction.TxCashAccount?.Id,
                LoadTradesAsync: LoadTradesAsync,
                ReloadAccountBalancesAsync: ReloadAccountBalancesAsync,
                RebuildTotals: RebuildTotals));
        Loan.LoanChanged += OnLoanChanged;

        // Wire the AddAssetDialog and SellPanel delegates that reference Transaction properties
        // now that Transaction is constructed.
        AddAssetDialog.GetTxCommissionDiscountValue = () => Transaction.TxCommissionDiscountValue;
        AddAssetDialog.GetTxFee = () => Transaction.TxFee;
        AddAssetDialog.GetTxBuyMetaOnly = () => Transaction.TxBuyMetaOnly;
        AddAssetDialog.GetTxCashAccountId = () => Transaction.TxCashAccount?.Id;
        AddAssetDialog.GetTxUseCashAccount = () => Transaction.TxUseCashAccount;
        SellPanel.GetTxCommissionDiscountValue = () => Transaction.TxCommissionDiscountValue;
        SellPanel.GetTxFee = () => Transaction.TxFee;

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

        services.Stock.QuoteStream
            .ObserveOn(ui.Scheduler)
            .Subscribe(UpdatePrices)
            .DisposeWith(_disposables);
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
            if (!latestDiscount.TryGetValue(row.Symbol, out var disc))
                continue;
            if (row.CommissionDiscount == disc)
                continue;
            row.CommissionDiscount = disc;
            row.Refresh();
        }
    }

    private async Task LoadPositionsAsync()
    {
        var loaded = await _loadService.LoadAsync();
        ApplyPositions(loaded);
    }

    private void ApplyPositions(PortfolioLoadResult loaded)
    {
        var entries = loaded.Entries;
        var snapshots = loaded.PositionSnapshots;
        // Build the new desired set of rows keyed by (Symbol, AssetType).
        var newRows = new Dictionary<(string Symbol, AssetType AssetType), PortfolioRowViewModel>();

        foreach (var g in entries.GroupBy(e => (e.Symbol, e.AssetType)))
        {
            var lots = g.ToList();
            var primary = lots[0];

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

            newRows[(g.Key.Symbol, g.Key.AssetType)] = row;
        }

        // Diff against current Positions: remove stale rows (iterate backward to avoid index shift),
        // then add new rows, then update in-place for rows that exist in both sets.
        var existingIndex = new Dictionary<(string Symbol, AssetType AssetType), PortfolioRowViewModel>();
        foreach (var r in Positions)
            existingIndex[(r.Symbol, r.AssetType)] = r;

        // Remove rows that are no longer in the new set (backward iteration).
        for (var i = Positions.Count - 1; i >= 0; i--)
        {
            var key = (Positions[i].Symbol, Positions[i].AssetType);
            if (!newRows.ContainsKey(key))
                Positions.RemoveAt(i);
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
                existing.AllEntryIds.Clear();
                foreach (var id in newRow.AllEntryIds)
                    existing.AllEntryIds.Add(id);
                existing.Refresh();
            }
            else
            {
                Positions.Add(newRow);
            }
        }

        HasNoPositions = Positions.Count == 0;

        // Refresh Lazy-Upsert suggestion list for TX form editable ComboBox (Task 19).
        // Only active entries — one row per (Symbol, Exchange).
        // Transaction sub-VM owns PositionSuggestions — update it here since ApplyPositions
        // is the only caller that has the full entries list.
        if (Transaction is not null)
        {
            Transaction.PositionSuggestions.Clear();
            foreach (var e in entries.Where(x => x.IsActive)
                                      .GroupBy(x => (x.Symbol, x.Exchange))
                                      .Select(g => g.First())
                                      .OrderBy(x => x.Symbol))
            {
                Transaction.PositionSuggestions.Add(
                    new SubViewModels.TransactionDialogViewModel.PositionSuggestion(
                        e.Id, e.Symbol, e.Exchange, e.DisplayName));
            }
        }
    }

    /// <summary>
    /// When true, archived (soft-deleted) positions are shown in the Positions list.
    /// Plan Task 15 + Task 20 (XAML toggle to be wired later).
    /// </summary>
    [ObservableProperty] private bool _showArchivedPositions;

    /// <summary>
    /// When true, positions with zero projected quantity (fully sold-out or 'watchlist')
    /// are hidden from the Positions list.
    /// </summary>
    [ObservableProperty] private bool _hideEmptyPositions;

    partial void OnShowArchivedPositionsChanged(bool value) => _ = LoadPositionsAsync();
    partial void OnHideEmptyPositionsChanged(bool value) => _ = LoadPositionsAsync();

    public async Task LoadAsync()
    {
        // Bypass OnMonthlyExpenseChanged (avoids premature save); RebuildTotals() below reads MonthlyExpense.
        // Restore persisted monthly expense (set via property to avoid triggering save-back on load)
        SetProperty(ref _monthlyExpense, _settingsService?.Current?.MonthlyExpense ?? 0m, nameof(MonthlyExpense));
        ShowWelcomeBanner = !(_settingsService?.Current?.HasShownWelcomeBanner ?? false);

        var loaded = await _loadService.LoadAsync();

        ApplyPositions(loaded);
        ApplyCashAccounts(loaded);
        ApplyLiabilities(loaded);

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
        _ = BackfillAndRefreshAsync();
    }

    // Named handlers so they can be unsubscribed in Dispose(); fire-and-forget with
    // _ = to make the async exception path explicit rather than relying on async void.
    private void OnAssetAdded(object? sender, EventArgs e)
        => _ = ReloadAfterAssetAddedAsync();

    private void OnSellCompleted(object? sender, EventArgs e)
        => _ = ReloadAfterSellAsync();

    private void OnTransactionCompleted(object? sender, EventArgs e)
        => _ = ReloadAfterTransactionAsync();

    private void OnTradeDeleted(object? sender, EventArgs e)
        => _ = ReloadAfterTradeDeletedAsync();

    private void OnAccountChanged(object? sender, EventArgs e)
        => _ = ReloadAfterAccountChangedAsync();

    private void OnLoanChanged(object? sender, EventArgs e)
        => _ = ReloadAfterLoanChangedAsync();

    /// <summary>
    /// Called by <see cref="SellPanelViewModel.SellCompleted"/> to refresh all
    /// position, trade, balance, and totals state after a successful sell.
    /// </summary>
    private async Task ReloadAfterSellAsync()
    {
        await LoadPositionsAsync();
        await LoadTradesAsync();
        await ReloadAccountBalancesAsync();
        RebuildTotals();
    }

    /// <summary>
    /// Called by <see cref="SubViewModels.TransactionDialogViewModel.TransactionCompleted"/> to refresh
    /// all position, trade, balance, and totals state after a successful transaction.
    /// </summary>
    private async Task ReloadAfterTransactionAsync()
    {
        await LoadPositionsAsync();
        await LoadTradesAsync();
        await ReloadAccountBalancesAsync();
        RebuildTotals();
    }

    /// <summary>
    /// Called by <see cref="SubViewModels.TransactionDialogViewModel.TradeDeleted"/> to refresh all
    /// position, trade, balance, and totals state after a successful trade deletion.
    /// </summary>
    private async Task ReloadAfterTradeDeletedAsync()
    {
        await LoadPositionsAsync();
        await LoadTradesAsync();
        await ReloadAccountBalancesAsync();
        RebuildTotals();
    }

    /// <summary>
    /// Called by <see cref="SubViewModels.AccountDialogViewModel.AccountChanged"/> to
    /// refresh cash accounts, balances, and totals after a successful account mutation.
    /// </summary>
    private async Task ReloadAfterAccountChangedAsync()
    {
        await LoadCashAccountsAsync();
        RebuildTotals();
    }

    /// <summary>
    /// Called by <see cref="SubViewModels.LoanDialogViewModel.LoanChanged"/> to refresh
    /// trades, account balances, and totals after a successful loan payment.
    /// </summary>
    private async Task ReloadAfterLoanChangedAsync()
    {
        await LoadTradesAsync();
        await ReloadAccountBalancesAsync();
        RebuildTotals();
    }

    /// <summary>
    /// Called by <see cref="AddAssetDialogViewModel.AssetAdded"/> to refresh all
    /// position, trade, balance, and totals state after a successful add.
    /// </summary>
    private async Task ReloadAfterAssetAddedAsync()
    {
        await LoadPositionsAsync();
        RebuildTotals();
        await LoadTradesAsync();
        await ReloadAccountBalancesAsync();
        RebuildTotals();

        // Fetch live price for any newly-added crypto positions.
        await RefreshCryptoPricesAsync();
    }

    /// <summary>Test-only hook — lets tests re-populate the Trades collection after
    /// adding rows directly to the fake repo (bypassing the normal UI flow).</summary>
    internal Task LoadTradesAsyncForTest() => LoadTradesAsync();

    private async Task LoadTradesAsync()
    {
        var loaded = await _loadService.LoadAsync();
        ApplyTrades(loaded.Trades);
    }

    private void ApplyTrades(IReadOnlyList<Trade> trades)
    {
        // 首次呼叫時建立 type filter 項目（需要 Application resources 已就緒）
        TradeFilter.InitTradeTypeFilters();
        try
        {
            Trades.Clear();
            foreach (var t in trades)
                Trades.Add(new TradeRowViewModel(t));
            HasNoTrades = Trades.Count == 0;
            HasAnyDividendTrades = Trades.Any(t => t.IsCashDividend);

            // Rebuild asset filter items — 依交易類型將 symbol 歸到
            // 投資（Buy/Sell/股利）/ 現金（Deposit/Withdrawal/Income/Interest）/ 負債（Loan*）
            TradeFilter.RebuildTradeAssetFilters();

            RebuildRealizedPnl();   // also calls TradeFilter.TradesView?.Refresh()
            TradeFilter.RefreshTradesView();

            // 以最新一筆 Buy 的折扣套到對應持倉，讓預估賣出費用反映當前券商折扣
            ApplyLatestTradeDiscounts();
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


    // Receives every QuoteStream tick and updates matching portfolio rows
    private void UpdatePrices(IReadOnlyList<StockQuote> quotes)
    {
        bool changed = false;
        foreach (var quote in quotes)
        {
            var row = Positions.FirstOrDefault(p => p.Symbol == quote.Symbol);
            if (row is null)
                continue;

            if (string.IsNullOrEmpty(row.Name) && !string.IsNullOrEmpty(quote.Name))
                row.Name = quote.Name;

            row.CurrentPrice = quote.Price;
            row.PrevClose = quote.PrevClose;
            row.IsLoadingPrice = false;
            row.Refresh();
            changed = true;
        }
        if (changed)
            RebuildTotals();
    }


    private void RebuildTotals()
    {
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
        TotalLiabilities = summary.TotalLiabilities;
        TotalAssets = summary.TotalAssets;
        NetWorth = summary.NetWorth;
        HasDayPnl = summary.HasDayPnl;
        DayPnl = summary.DayPnl;
        _dayPnlPercent = summary.DayPnlPercent;
        OnPropertyChanged(nameof(DayPnlPercentDisplay));
        IsDayPnlPositive = summary.IsDayPnlPositive;

        // Recompute cached financial summary properties and notify all dependents
        ApplyFinancialSummary(summary);

        RebuildAllocationSlices(summary.AllocationSlices);

        // Fire-and-forget: record today's snapshot once prices are live
        _ = RecordSnapshotAsync();
    }

    /// <summary>
    /// Recomputes all cached financial-summary properties that depend on Liabilities,
    /// TotalAssets, TotalLiabilities, TotalCash, NetWorth, or MonthlyExpense, then
    /// fires PropertyChanged for every downstream display property.
    /// Call this from RebuildTotals() and OnMonthlyExpenseChanged().
    /// </summary>
    private void ApplyFinancialSummary(PortfolioSummaryResult summary)
    {
        TotalOriginalLiabilities = summary.TotalOriginalLiabilities;
        DebtRatioValue = summary.DebtRatioValue;
        PaidPercentValue = summary.PaidPercentValue;
        EmergencyFundMonths = summary.EmergencyFundMonths;

        // Notify derived display properties (the backing-field setters above already
        // raise PropertyChanged for the four cached properties themselves)
        OnPropertyChanged(nameof(DebtRatioDisplay));
        OnPropertyChanged(nameof(LeverageRatioDisplay));
        OnPropertyChanged(nameof(IsDebtHealthy));
        OnPropertyChanged(nameof(IsDebtWarning));
        OnPropertyChanged(nameof(IsDebtDanger));
        OnPropertyChanged(nameof(DebtStatusTag));
        OnPropertyChanged(nameof(PaidPercentDisplay));
        OnPropertyChanged(nameof(TotalOriginalDisplay));
        OnPropertyChanged(nameof(EmergencyFundMonthsDisplay));
        OnPropertyChanged(nameof(EmergencyFundBarValue));
        OnPropertyChanged(nameof(IsEmergencySafe));
        OnPropertyChanged(nameof(IsEmergencyWarning));
        OnPropertyChanged(nameof(IsEmergencyDanger));
        OnPropertyChanged(nameof(EmergencyStatusTag));
    }

    private static readonly IReadOnlyDictionary<AssetType, (string LabelKey, string Color)> AssetTypeColors =
        new Dictionary<AssetType, (string, string)>
        {
            [AssetType.Stock] = ("Portfolio.AssetType.Stock", "#3B82F6"),
            [AssetType.Fund] = ("Portfolio.AssetType.Fund", "#10B981"),
            [AssetType.PreciousMetal] = ("Portfolio.AssetType.Metal", "#F59E0B"),
            [AssetType.Bond] = ("Portfolio.AssetType.Bond", "#6B7280"),
            [AssetType.Crypto] = ("Portfolio.AssetType.Crypto", "#8B5CF6"),
        };

    private void RebuildAllocationSlices(IReadOnlyList<AllocationSliceResult> slices)
    {
        // Build the new slice list without touching the observable collection yet.
        var newSlices = new List<AssetAllocationSlice>();

        foreach (var slice in slices)
        {
            switch (slice.Kind)
            {
                case AllocationSliceKind.AssetType when slice.AssetType is AssetType assetType:
                    if (!AssetTypeColors.TryGetValue(assetType, out var meta))
                        continue;
                    var label = L(meta.LabelKey, assetType.ToString());
                    newSlices.Add(new AssetAllocationSlice(label, slice.Value, slice.Percent, meta.Color));
                    break;
                case AllocationSliceKind.Cash:
                    newSlices.Add(new AssetAllocationSlice(
                        L("Portfolio.Header.Cash", "Cash"),
                        slice.Value,
                        slice.Percent,
                        "#94A3B8"));
                    break;
                case AllocationSliceKind.Liabilities:
                    newSlices.Add(new AssetAllocationSlice(
                        L("Portfolio.Header.Liabilities", "Liabilities"),
                        slice.Value,
                        slice.Percent,
                        "#EF4444"));
                    break;
            }
        }

        // Dirty-check: only rebuild AllocationPieSeries when slice data has materially changed.
        // LiveCharts treats a new ISeries[] reference as a full chart reset (animation flicker + GC).
        // Comparing by label + value (rounded to nearest integer) avoids churn on tiny price ticks.
        bool slicesChanged = newSlices.Count != AllocationSlices.Count;
        if (!slicesChanged)
        {
            for (var i = 0; i < newSlices.Count; i++)
            {
                var n = newSlices[i];
                var o = AllocationSlices[i];
                if (n.Label != o.Label || Math.Round(n.Value) != Math.Round(o.Value))
                {
                    slicesChanged = true;
                    break;
                }
            }
        }

        if (!slicesChanged)
            return;

        // Slices changed — update observable collection and rebuild PieSeries array.
        AllocationSlices.Clear();
        foreach (var s in newSlices)
            AllocationSlices.Add(s);

        if (newSlices.Count == 0)
        {
            AllocationPieSeries = [];
            OnPropertyChanged(nameof(HasAllocationData));
            return;
        }

        // Build PieSeries for LiveChartsCore v2 (must use double, not decimal)
        AllocationPieSeries = AllocationSlices
            .Select(s =>
            {
                var paint = new LiveChartsCore.SkiaSharpView.Painting.SolidColorPaint(SKColor.Parse(s.ColorHex));
                return (ISeries)new PieSeries<double>
                {
                    Values = new[] { (double)s.Value },
                    // Name drives the LiveChartsCore tooltip (built-in legend is hidden).
                    // Embed value + percent so the tooltip popup shows useful info on hover.
                    Name = $"{s.Label}  NT${s.Value:N0}  ({s.Percent:F1}%)",
                    InnerRadius = 40,
                    Fill = paint,
                    Stroke = null,
                    DataLabelsSize = 0,
                    HoverPushout = 6,
                    AnimationsSpeed = TimeSpan.Zero,
                };
            })
            .ToArray();

        OnPropertyChanged(nameof(HasAllocationData));
    }

    private async Task RecordSnapshotAsync()
    {
        try
        {
            var written = await _historyMaintenanceService.TryRecordSnapshotAsync(
                TotalCost, TotalMarketValue, TotalPnl, Positions.Count);
            if (written)
                await History.LoadAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[Portfolio] RecordSnapshotAsync failed");
        }
    }

    /// <summary>
    /// Runs backfill in the background; reloads chart when at least one gap is filled.
    /// </summary>
    private async Task BackfillAndRefreshAsync()
    {
        try
        {
            var written = await _historyMaintenanceService.BackfillAsync();
            if (written > 0)
                await History.LoadAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[Portfolio] BackfillAndRefreshAsync failed");
        }
    }

    // Position log helpers — seeding helpers removed in Wave 9.3: the trade log is now
    // the single source of truth. SeedPositionLogAsync, SeedBuyTradesAsync, and
    // MigrateTradePortfolioLinksAsync have been deleted.

    /// <summary>
    /// Re-projects every cash and liability account balance from the trade history.
    /// Call after any operation that adds / removes / edits a trade so the detail panel
    /// and totals reflect the new state without reloading the page.
    /// Safe to call when repos are null (tests) — just no-ops.
    /// </summary>
    private async Task ReloadAccountBalancesAsync()
    {
        var loaded = await _loadService.LoadAsync();
        ApplyCashAccounts(loaded);
        ApplyLiabilities(loaded);
        // Caller must invoke RebuildTotals() after this to keep RecalcFinancialSummary up-to-date.
    }

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

    /// <summary>
    /// Fetches live TWD prices for all Crypto positions and updates <see cref="CurrentPrice"/>.
    /// No-ops gracefully if no crypto service is registered or no crypto positions exist.
    /// </summary>
    private async Task RefreshCryptoPricesAsync()
    {
        if (_cryptoService is null)
            return;
        var cryptoRows = Positions.Where(p => p.AssetType == AssetType.Crypto).ToList();
        if (cryptoRows.Count == 0)
            return;

        var symbols = cryptoRows.Select(r => r.Symbol).Distinct().ToList();
        var prices = await _cryptoService.GetPricesTwdAsync(symbols);
        if (prices.Count == 0)
            return;

        foreach (var row in cryptoRows)
        {
            if (!prices.TryGetValue(row.Symbol, out var price))
                continue;
            row.CurrentPrice = price;
            row.IsLoadingPrice = false;
            row.Refresh();
        }
        RebuildTotals();
    }

    private void OnCurrencyChanged()
    {
        foreach (var row in Positions)
            row.NotifyCurrencyChanged();
        foreach (var trade in Trades)
            trade.NotifyCurrencyChanged();
        foreach (var cash in CashAccounts)
            cash.NotifyCurrencyChanged();
        foreach (var liab in Liabilities)
            liab.NotifyCurrencyChanged();

        OnPropertyChanged(nameof(TotalCost));
        OnPropertyChanged(nameof(TotalMarketValue));
        OnPropertyChanged(nameof(TotalPnl));
        OnPropertyChanged(nameof(DayPnl));
        OnPropertyChanged(nameof(TotalRealizedPnl));
        OnPropertyChanged(nameof(TotalCash));
        OnPropertyChanged(nameof(TotalLiabilities));
        OnPropertyChanged(nameof(TotalAssets));
        OnPropertyChanged(nameof(NetWorth));
        SellPanel.NotifyCurrencyChanged();
        ApplyFinancialSummary(_summaryService.Calculate(BuildSummaryInput()));
    }

    private PortfolioSummaryInput BuildSummaryInput()
    {
        return new PortfolioSummaryInput(
            Positions.Select(p => new PositionSummaryInput(
                p.Id,
                p.AssetType,
                p.Quantity,
                p.Cost,
                p.MarketValue,
                p.NetValue,
                p.CurrentPrice,
                p.PrevClose,
                p.IsLoadingPrice)).ToList(),
            CashAccounts.Select(c => new CashBalanceInput(c.Id, c.Balance)).ToList(),
            Liabilities.Select(l => new LiabilityBalanceInput(
                l.AssetId ?? Guid.Empty,
                l.Balance,
                l.OriginalAmount)).ToList(),
            MonthlyExpense);
    }

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
        // Open Tx dialog in Sell mode with this position pre-selected
        Transaction.OpenTxDialog();
        Transaction.TxType = "sell";
        Transaction.TxSellPosition = row;
        Transaction.TxSellQuantity = ((int)row.Quantity).ToString();
    }

    /// <summary>側面板「買入」快速動作 — 打開 Tx 對話框，預填當前股票代號。</summary>
    [RelayCommand]
    private void BeginBuyForSelectedPosition()
    {
        if (SelectedPositionRow is null)
            return;
        var row = SelectedPositionRow;
        Transaction.OpenTxDialog();
        Transaction.TxType = "buy";
        Transaction.TxBuyAssetType = "stock";
        AddAssetDialog.AddSymbol = row.Symbol;
        AddAssetDialog.AddPrice = string.Empty;
        AddAssetDialog.AddQuantity = string.Empty;
    }

    /// <summary>側面板「配息入帳」快速動作 — 打開 Tx 對話框並預選此持倉。</summary>
    [RelayCommand]
    private void BeginDividendForSelectedPosition()
    {
        if (SelectedPositionRow is null)
            return;
        Transaction.OpenTxDialog();
        Transaction.TxType = "cashDiv";
        Transaction.TxDivPosition = SelectedPositionRow;
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

    /// <summary>開啟新增現金帳戶對話框（由現金 tab 的「新增帳戶」按鈕呼叫）。</summary>
    [RelayCommand]
    private void OpenAddAccountDialog()
    {
        SelectedTab = PortfolioTab.Accounts;
        AddAssetDialog.AddAssetType = "cash";
        AddAssetDialog.AddError = string.Empty;
        AddAssetDialog.AddAccountName = string.Empty;
        AddAssetDialog.IsAddDialogOpen = true;
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
        var accounts = loaded.CashAccounts;
        var balances = loaded.CashBalances;
        var visibleAccounts = accounts
            .Where(a => Account.ShowArchivedAccounts || a.IsActive)
            .ToList();
        var newRows = visibleAccounts.ToDictionary(
            a => a.Id,
            a => new CashAccountRowViewModel(a, balances.TryGetValue(a.Id, out var v) ? v : 0m));
        var existingIndex = CashAccounts.ToDictionary(r => r.Id);

        for (var i = CashAccounts.Count - 1; i >= 0; i--)
        {
            if (!newRows.ContainsKey(CashAccounts[i].Id))
                CashAccounts.RemoveAt(i);
        }

        foreach (var account in visibleAccounts)
        {
            var bal = balances.TryGetValue(account.Id, out var v) ? v : 0m;
            if (existingIndex.TryGetValue(account.Id, out var existing))
            {
                existing.Name = account.Name;
                existing.Currency = account.Currency;
                existing.Balance = bal;
                existing.IsActive = account.IsActive;
            }
            else
            {
                CashAccounts.Add(newRows[account.Id]);
            }
        }
        HasNoCashAccounts = CashAccounts.Count == 0;

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
            Transaction.CashAccountSuggestions.Clear();
            foreach (var a in accounts.Where(a => a.IsActive).OrderBy(a => a.Name))
                Transaction.CashAccountSuggestions.Add(a.Name);
        }
    }

    // ── Liability loading ─────────────────────────────────────────────────────────────

    private async Task LoadLiabilitiesAsync()
    {
        var loaded = await _loadService.LoadAsync();
        ApplyLiabilities(loaded);
    }

    private void ApplyLiabilities(PortfolioLoadResult loaded)
    {
        var snapshots = loaded.LiabilitySnapshots;
        var loanAssets = loaded.LoanAssets;

        Liabilities.Clear();
        foreach (var (label, snap) in snapshots.OrderBy(kv => kv.Key))
        {
            loanAssets.TryGetValue(label, out var asset);
            Liabilities.Add(new LiabilityRowViewModel(label, snap, asset));
        }

        foreach (var (name, asset) in loanAssets)
        {
            if (!snapshots.ContainsKey(name))
                Liabilities.Add(new LiabilityRowViewModel(name, LiabilitySnapshot.Empty, asset));
        }

        HasNoLiabilities = Liabilities.Count == 0;
        Transaction?.NotifyLoanLabelSuggestionsChanged();
    }

    // ── Account detail side-panel ─────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedCashRow))]
    [NotifyPropertyChangedFor(nameof(SelectedCashTrades))]
    [NotifyPropertyChangedFor(nameof(SelectedCashTotalDeposits))]
    [NotifyPropertyChangedFor(nameof(SelectedCashTotalWithdrawals))]
    private CashAccountRowViewModel? _selectedCashRow;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedLiabilityRow))]
    [NotifyPropertyChangedFor(nameof(SelectedLiabilityTrades))]
    [NotifyPropertyChangedFor(nameof(SelectedLiabilityTotalBorrows))]
    [NotifyPropertyChangedFor(nameof(SelectedLiabilityTotalRepays))]
    private LiabilityRowViewModel? _selectedLiabilityRow;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedPositionRow))]
    [NotifyPropertyChangedFor(nameof(SelectedPositionTrades))]
    [NotifyPropertyChangedFor(nameof(SelectedPositionDividendIncome))]
    [NotifyPropertyChangedFor(nameof(SelectedPositionRealizedTotal))]
    [NotifyPropertyChangedFor(nameof(SelectedPositionTradeAvgPrice))]
    [NotifyPropertyChangedFor(nameof(HasSelectedPositionRealized))]
    private PortfolioRowViewModel? _selectedPositionRow;

    /// <summary>
    /// 是否有任何已實現損益資料（賣出價差或股息收入）。
    /// </summary>
    public bool HasSelectedPositionRealized =>
        SelectedPositionRealizedTotal != 0m || SelectedPositionDividendIncome > 0m;

    public bool HasSelectedCashRow => SelectedCashRow is not null;
    public bool HasSelectedLiabilityRow => SelectedLiabilityRow is not null;
    public bool HasSelectedPositionRow => SelectedPositionRow is not null;

    /// <summary>
    /// Detail-panel active tab ("overview" or "trades"). Shared between the Cash and
    /// Liability panels — each panel only shows at most one at a time so a single
    /// property is sufficient; resets to "overview" whenever the selected row changes.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDetailOverviewTab))]
    [NotifyPropertyChangedFor(nameof(IsDetailTradesTab))]
    [NotifyPropertyChangedFor(nameof(IsDetailScheduleTab))]
    private string _detailTab = "overview";

    public bool IsDetailOverviewTab => DetailTab == "overview";
    public bool IsDetailTradesTab => DetailTab == "trades";
    public bool IsDetailScheduleTab => DetailTab == "schedule";

    partial void OnSelectedCashRowChanged(CashAccountRowViewModel? _) => DetailTab = "overview";
    partial void OnSelectedLiabilityRowChanged(LiabilityRowViewModel? row)
    {
        DetailTab = "overview";
        if (row is { IsLoan: true, IsScheduleLoaded: false })
            _ = Loan.LoadLoanScheduleAsync(row);
    }
    partial void OnSelectedPositionRowChanged(PortfolioRowViewModel? _) => DetailTab = "overview";

    // Cash account stats + filtered trades
    public IEnumerable<TradeRowViewModel> SelectedCashTrades =>
        SelectedCashRow is { } r
            ? Trades.Where(t => t.CashAccountId == r.Id)
                    .OrderByDescending(t => t.TradeDate)
            : [];

    public decimal SelectedCashTotalDeposits =>
        SelectedCashRow is { } r
            ? Trades.Where(t => t.CashAccountId == r.Id &&
                                (t.Type == TradeType.Deposit ||
                                 t.Type == TradeType.Income ||
                                 t.Type == TradeType.CashDividend ||
                                 t.Type == TradeType.LoanBorrow ||
                                 t.Type == TradeType.Sell))
                    .Sum(t => Math.Abs(t.CashAmount ?? 0))
            : 0m;

    public decimal SelectedCashTotalWithdrawals =>
        SelectedCashRow is { } r
            ? Trades.Where(t => t.CashAccountId == r.Id &&
                                (t.Type == TradeType.Withdrawal ||
                                 t.Type == TradeType.Buy ||
                                 t.Type == TradeType.LoanRepay))
                    .Sum(t => Math.Abs(t.CashAmount ?? 0))
            : 0m;

    // Liability stats + filtered trades
    public IEnumerable<TradeRowViewModel> SelectedLiabilityTrades =>
        SelectedLiabilityRow is { } r
            ? Trades.Where(t => (t.Type == TradeType.LoanBorrow || t.Type == TradeType.LoanRepay) &&
                                t.LoanLabel == r.Label)
                    .OrderByDescending(t => t.TradeDate)
            : [];

    public decimal SelectedLiabilityTotalBorrows =>
        SelectedLiabilityRow is { } r
            ? Trades.Where(t => t.Type == TradeType.LoanBorrow && t.LoanLabel == r.Label)
                    .Sum(t => t.CashAmount ?? 0)
            : 0m;

    public decimal SelectedLiabilityTotalRepays =>
        SelectedLiabilityRow is { } r
            ? Trades.Where(t => t.Type == TradeType.LoanRepay && t.LoanLabel == r.Label)
                    .Sum(t => t.CashAmount ?? 0)
            : 0m;

    // Investment position stats + filtered trades
    public IEnumerable<TradeRowViewModel> SelectedPositionTrades =>
        SelectedPositionRow is { } r
            ? Trades.Where(t => string.Equals(t.Symbol, r.Symbol, StringComparison.OrdinalIgnoreCase) &&
                                (t.Type == TradeType.Buy || t.Type == TradeType.Sell ||
                                 t.Type == TradeType.CashDividend || t.Type == TradeType.StockDividend))
                    .OrderByDescending(t => t.TradeDate)
            : [];

    /// <summary>
    /// 成交均價 — 以 Buy 交易紀錄的股數加權平均，**不含買入手續費**。
    /// </summary>
    public decimal SelectedPositionTradeAvgPrice
    {
        get
        {
            if (SelectedPositionRow is not { } r)
                return 0m;
            var buys = Trades
                .Where(t => t.IsBuy && string.Equals(t.Symbol, r.Symbol, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (buys.Count == 0)
                return 0m;
            var totalQty = buys.Sum(t => (decimal)t.Quantity);
            var totalGross = buys.Sum(t => t.Price * t.Quantity);
            return totalQty > 0 ? totalGross / totalQty : 0m;
        }
    }

    /// <summary>Sum of all CashDividend trades for the selected position (gross dividend income).</summary>
    public decimal SelectedPositionDividendIncome =>
        SelectedPositionRow is { } r
            ? Trades.Where(t => string.Equals(t.Symbol, r.Symbol, StringComparison.OrdinalIgnoreCase) &&
                                t.Type == TradeType.CashDividend)
                    .Sum(t => t.CashAmount ?? 0)
            : 0m;

    /// <summary>
    /// Realized capital gain — placeholder 0 so the realized P&L card layout renders correctly.
    /// </summary>
    public decimal SelectedPositionCapitalGain => 0m;

    public decimal SelectedPositionRealizedTotal =>
        SelectedPositionDividendIncome + SelectedPositionCapitalGain;

    [RelayCommand]
    private void CloseCashDetail() => SelectedCashRow = null;

    [RelayCommand]
    private void CloseLiabilityDetail() => SelectedLiabilityRow = null;

    [RelayCommand]
    private void ClosePositionDetail() => SelectedPositionRow = null;

    /// <summary>
    /// Permanently removes a position and all its associated Buy / StockDividend trade
    /// records (plus their fee children).
    /// </summary>
    [RelayCommand]
    private void RemovePosition(PortfolioRowViewModel row)
    {
        if (row is null)
            return;
        var msg = L("Portfolio.Detail.DeleteWarning", "刪除後無法復原，請確認不再需要後再操作。");
        AskConfirm(msg, async () =>
        {
            await _positionDeletionWorkflowService.DeleteAsync(
                new PositionDeletionRequest(row.AllEntryIds.ToList()));

            Positions.Remove(row);
            if (ReferenceEquals(SelectedPositionRow, row))
                SelectedPositionRow = null;
            RebuildTotals();

            await LoadTradesAsync();
            await ReloadAccountBalancesAsync();
        });
    }

    [RelayCommand]
    private void SwitchDetailTab(string tab) => DetailTab = tab;

    public void Dispose()
    {
        if (_currencyService is not null)
            _currencyService.CurrencyChanged -= OnCurrencyChanged;
        if (_themeService is not null && _onThemeChanged is not null)
            _themeService.ThemeChanged -= _onThemeChanged;
        AddAssetDialog.AssetAdded -= OnAssetAdded;
        AddAssetDialog.CancelPendingFetch();
        SellPanel.SellCompleted -= OnSellCompleted;
        Transaction.TransactionCompleted -= OnTransactionCompleted;
        Transaction.TradeDeleted -= OnTradeDeleted;
        Account.AccountChanged -= OnAccountChanged;
        Loan.LoanChanged -= OnLoanChanged;
        _disposables.Dispose();
    }

    // Null-object fallback when DI doesn't wire trade repo (tests / legacy)

    private sealed class NullTradeRepository : ITradeRepository
    {
        public Task<IReadOnlyList<Trade>> GetAllAsync() =>
            Task.FromResult<IReadOnlyList<Trade>>([]);
        public Task<IReadOnlyList<Trade>> GetByCashAccountAsync(Guid cashAccountId) =>
            Task.FromResult<IReadOnlyList<Trade>>([]);
        public Task<IReadOnlyList<Trade>> GetByLoanLabelAsync(string loanLabel) =>
            Task.FromResult<IReadOnlyList<Trade>>([]);
        public Task AddAsync(Trade trade) => Task.CompletedTask;
        public Task UpdateAsync(Trade trade) => Task.CompletedTask;
        public Task RemoveAsync(Guid id) => Task.CompletedTask;
        public Task RemoveChildrenAsync(Guid parentId) => Task.CompletedTask;
    }

    // Fallback when DI doesn't wire IBalanceQueryService (tests that only populate
    // stored account rows without a trade history). All balances return 0 / empty.
    private sealed class NullBalanceQueryService : IBalanceQueryService
    {
        public Task<decimal> GetCashBalanceAsync(Guid cashAccountId) =>
            Task.FromResult(0m);
        public Task<LiabilitySnapshot> GetLiabilitySnapshotAsync(string loanLabel) =>
            Task.FromResult(LiabilitySnapshot.Empty);
        public Task<IReadOnlyDictionary<Guid, decimal>> GetAllCashBalancesAsync() =>
            Task.FromResult<IReadOnlyDictionary<Guid, decimal>>(
                new Dictionary<Guid, decimal>());
        public Task<IReadOnlyDictionary<string, LiabilitySnapshot>> GetAllLiabilitySnapshotsAsync() =>
            Task.FromResult<IReadOnlyDictionary<string, LiabilitySnapshot>>(
                new Dictionary<string, LiabilitySnapshot>());
    }

    // Fallback when DI doesn't wire IPositionQueryService (tests that don't seed trades).
    // All snapshots return 0 / empty.
    private sealed class NullPositionQueryService : IPositionQueryService
    {
        public Task<PositionSnapshot?> GetPositionAsync(Guid portfolioEntryId) =>
            Task.FromResult<PositionSnapshot?>(null);
        public Task<IReadOnlyDictionary<Guid, PositionSnapshot>> GetAllPositionSnapshotsAsync() =>
            Task.FromResult<IReadOnlyDictionary<Guid, PositionSnapshot>>(
                new Dictionary<Guid, PositionSnapshot>());
        public Task<decimal> ComputeRealizedPnlAsync(
            Guid portfolioEntryId, DateTime sellDate, decimal sellPrice,
            decimal sellQty, decimal sellFees) =>
            Task.FromResult(0m);
    }

    private sealed class NullPortfolioHistoryMaintenanceService : IPortfolioHistoryMaintenanceService
    {
        public Task<bool> TryRecordSnapshotAsync(
            decimal totalCost,
            decimal marketValue,
            decimal pnl,
            int positionCount,
            CancellationToken ct = default) =>
            Task.FromResult(false);

        public Task<int> BackfillAsync(CancellationToken ct = default) =>
            Task.FromResult(0);
    }

    private sealed class NullAccountMutationWorkflowService : IAccountMutationWorkflowService
    {
        public Task ArchiveAsync(Guid accountId, CancellationToken ct = default) => Task.CompletedTask;

        public Task<AccountDeletionResult> DeleteAsync(Guid accountId, CancellationToken ct = default) =>
            Task.FromResult(new AccountDeletionResult(false));
    }

    private sealed class NullAccountUpsertWorkflowService : IAccountUpsertWorkflowService
    {
        public Task<AccountUpsertResult> CreateAsync(CreateAccountRequest request, CancellationToken ct = default) =>
            Task.FromResult(new AccountUpsertResult(
                new AssetItem(Guid.NewGuid(), request.Name, FinancialType.Asset, null, request.Currency, request.CreatedDate)));

        public Task<AccountUpsertResult> UpdateAsync(UpdateAccountRequest request, CancellationToken ct = default) =>
            Task.FromResult(new AccountUpsertResult(
                new AssetItem(request.AccountId, request.Name, FinancialType.Asset, null, request.Currency, request.CreatedDate)));
    }

    private sealed class NullLoanPaymentWorkflowService : ILoanPaymentWorkflowService
    {
        public Task<LoanPaymentResult> RecordAsync(LoanPaymentRequest request, CancellationToken ct = default) =>
            Task.FromResult(new LoanPaymentResult(
                new Trade(
                    Guid.NewGuid(),
                    string.Empty,
                    string.Empty,
                    request.LoanLabel,
                    TradeType.LoanRepay,
                    request.TradeDate,
                    request.Entry.PrincipalAmount + request.Entry.InterestAmount,
                    1,
                    0m,
                    0m,
                    request.Entry.PrincipalAmount + request.Entry.InterestAmount,
                    request.CashAccountId,
                    LoanLabel: request.LoanLabel,
                    Principal: request.Entry.PrincipalAmount,
                    InterestPaid: request.Entry.InterestAmount),
                DateTime.UtcNow));
    }

    private sealed class NullLoanMutationWorkflowService : ILoanMutationWorkflowService
    {
        public Task<LoanMutationResult> RecordAsync(LoanTransactionRequest request, CancellationToken ct = default) =>
            Task.FromResult(new LoanMutationResult(null, null));
    }

}

