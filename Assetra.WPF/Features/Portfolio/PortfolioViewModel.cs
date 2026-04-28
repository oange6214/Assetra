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
    private readonly IStockSearchService _search;
    private readonly IAppSettingsService? _settingsService;
    private readonly ISnackbarService? _snackbar;
    private readonly IThemeService? _themeService;
    private Action<ApplicationTheme>? _onThemeChanged;
    private readonly ICurrencyService? _currencyService;
    private readonly ICryptoService? _cryptoService;
    private readonly IStockHistoryProvider? _historyProvider;
    private readonly IPortfolioLoadService _loadService;
    private readonly ITransactionWorkflowService _transactionWorkflowService;
    private readonly ITradeDeletionWorkflowService _tradeDeletionWorkflowService;
    private readonly PortfolioSellPanelController _sellPanelController = new();
    private readonly PortfolioTradeDialogController _tradeDialogController = new();
    private readonly IPositionDeletionWorkflowService _positionDeletionWorkflowService;
    private readonly ILiabilityMutationWorkflowService _liabilityMutationWorkflowService;
    private readonly IPortfolioSummaryService _summaryService;
    private readonly IPortfolioHistoryMaintenanceService _historyMaintenanceService;
    private readonly ILocalizationService? _localization;
    private readonly CompositeDisposable _disposables = new();

    public ObservableCollection<PortfolioRowViewModel> Positions { get; } = [];
    public ObservableCollection<TradeRowViewModel> Trades { get; } = [];
    public ObservableCollection<CashAccountRowViewModel> CashAccounts { get; } = [];
    public ObservableCollection<LiabilityRowViewModel> Liabilities { get; } = [];

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

    // 負債健康度 + 緊急預備金 — owned by FinancialSummaryViewModel.
    public FinancialSummaryViewModel Financial { get; }

    private void OnMonthlyExpenseFromSubVm(decimal value)
    {
        Financial.Apply(_summaryService.Calculate(BuildSummaryInput()));
        _ = SaveMonthlyExpenseAsync();
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
    public DividendCalendarViewModel DivCalendar { get; }

    public PortfolioViewModel(
        PortfolioServices services,
        PortfolioUiServices ui)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(ui);

        _search = services.Search;
        _snackbar = ui.Snackbar;
        _settingsService = ui.Settings;
        _currencyService = services.Currency;
        _cryptoService = services.Crypto;
        _historyProvider = services.History;
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
            ui.Localization);

        // TradeFilter must be created before LoadAsync so LoadTradesAsync can call
        // TradeFilter.InitTradeTypeFilters() and TradeFilter.RefreshTradesView().
        TradeFilter = new TradeFilterViewModel(() => Trades, ui.Localization ?? NullLocalizationService.Instance);
        TradeFilter.AttachTradesCollection(Trades);

        // Resolve workflow services for Sub-VM construction (not held as VM fields).
        var addAssetWorkflow = services.AddAsset ?? new NullAddAssetWorkflowService();
        var accountUpsertWorkflow = services.AccountUpsert ?? new NullAccountUpsertWorkflowService();
        var accountMutationWorkflow = services.AccountMutation ?? new NullAccountMutationWorkflowService();
        var creditCardMutationWorkflow = services.CreditCardMutation ?? new NullCreditCardMutationWorkflowService();
        var creditCardTransactionWorkflow = services.CreditCardTransaction ?? new NullCreditCardTransactionWorkflowService();
        var sellWorkflow = services.Sell ?? new NullSellWorkflowService();
        var tradeMetadataWorkflow = services.TradeMetadata ?? new NullTradeMetadataWorkflowService();
        var loanPaymentWorkflow = services.LoanPayment ?? new NullLoanPaymentWorkflowService();
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
                RebuildTotals: RebuildTotals,
                Localize: L,
                CategoryRepository: services.CategoryRepository,
                AutoCategorizationRuleRepository: services.AutoCategorizationRuleRepository));
        Transaction.TransactionCompleted += OnTransactionCompleted;
        Transaction.TradeDeleted += OnTradeDeleted;

        // Build the Loan sub-VM with delegates into parent state.
        Loan = services.Loan ?? new SubViewModels.LoanDialogViewModel(
            new SubViewModels.LoanDialogDependencies(
                LoanPayment: loanPaymentWorkflow,
                LoanSchedule: loanScheduleService,
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
        AddAssetDialog.GetCashAccounts = () => CashAccounts;
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
                ? new TradeDeletionWorkflowService(repositories.Trade, repositories.Portfolio, positionQuery)
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

    private void RaiseSetupNoticeChanged()
        => SetupNotice.Refresh(HasNoCashAccounts, HasNoTrades);

    public async Task LoadAsync()
    {
        // Bypass OnMonthlyExpenseChanged (avoids premature save); RebuildTotals() below reads MonthlyExpense.
        Financial.InitializeMonthlyExpense(_settingsService?.Current?.MonthlyExpense ?? 0m);

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
            RaiseSetupNoticeChanged();
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
        Financial.Apply(summary);

        Allocation.Apply(summary.AllocationSlices);

        // Fire-and-forget: record today's snapshot once prices are live
        _ = RecordSnapshotAsync();
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
            Financial.MonthlyExpense);
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
        AddAssetDialog.AddDialogMode = "account";
        AddAssetDialog.IsTypePickerStep = true;
        AddAssetDialog.AddError = string.Empty;
        AddAssetDialog.AddSubtype = string.Empty;
        AddAssetDialog.AddAccountName = string.Empty;
        AddAssetDialog.AddInitialDepositEnabled = false;
        AddAssetDialog.AddInitialDepositAmount = string.Empty;
        AddAssetDialog.AddInitialDepositDate = DateTime.Today;
        AddAssetDialog.AddInitialDepositNote = string.Empty;
        AddAssetDialog.IsAddDialogOpen = true;
    }

    [RelayCommand]
    private void OpenAddLiabilityDialog()
    {
        SelectedTab = PortfolioTab.Liability;
        AddAssetDialog.AddDialogMode = "liability";
        AddAssetDialog.IsTypePickerStep = true;
        AddAssetDialog.AddError = string.Empty;
        AddAssetDialog.AddLoanName = string.Empty;
        AddAssetDialog.AddLoanAmount = string.Empty;
        AddAssetDialog.AddLoanAnnualRate = string.Empty;
        AddAssetDialog.AddLoanTermMonths = string.Empty;
        AddAssetDialog.AddLoanHandlingFee = string.Empty;
        AddAssetDialog.AddLoanStartDate = DateTime.Today;
        AddAssetDialog.SelectedLoanCashAccount = null;
        AddAssetDialog.AddCreditCardName = string.Empty;
        AddAssetDialog.AddCreditCardIssuer = string.Empty;
        AddAssetDialog.AddCreditCardBillingDay = string.Empty;
        AddAssetDialog.AddCreditCardDueDay = string.Empty;
        AddAssetDialog.AddCreditCardLimit = string.Empty;
        AddAssetDialog.AddInitialCreditCardBalanceEnabled = false;
        AddAssetDialog.AddInitialCreditCardBalanceAmount = string.Empty;
        AddAssetDialog.AddInitialCreditCardBalanceDate = DateTime.Today;
        AddAssetDialog.AddInitialCreditCardBalanceNote = string.Empty;
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
        var liabilityAssets = loaded.LiabilityAssets;

        Liabilities.Clear();
        foreach (var (label, snap) in snapshots.OrderBy(kv => kv.Key))
        {
            liabilityAssets.TryGetValue(label, out var asset);
            Liabilities.Add(new LiabilityRowViewModel(label, snap, asset));
        }

        foreach (var (name, asset) in liabilityAssets)
        {
            if (!snapshots.ContainsKey(name))
                Liabilities.Add(new LiabilityRowViewModel(name, LiabilitySnapshot.Empty, asset));
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
        AddAssetDialog.AssetAdded -= OnAssetAdded;
        AddAssetDialog.CancelPendingFetch();
        SellPanel.SellCompleted -= OnSellCompleted;
        Transaction.TransactionCompleted -= OnTransactionCompleted;
        Transaction.TradeDeleted -= OnTradeDeleted;
        Account.AccountChanged -= OnAccountChanged;
        Loan.LoanChanged -= OnLoanChanged;
        _disposables.Dispose();
    }
}

