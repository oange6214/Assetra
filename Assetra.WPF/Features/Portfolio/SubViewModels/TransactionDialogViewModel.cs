using System.Collections.ObjectModel;
using System.Globalization;
using System.Reactive.Linq;
using Assetra.Application.Fx;
using Assetra.Application.Portfolio.Contracts;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Assetra.Core.Trading;
using Assetra.WPF.Features.Categories;
using Assetra.WPF.Features.Portfolio.SubViewModels.Tx;
using Assetra.WPF.Features.PortfolioGroups;
using Assetra.WPF.Infrastructure;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace Assetra.WPF.Features.Portfolio.SubViewModels;

/// <summary>
/// Dependencies passed to <see cref="TransactionDialogViewModel"/> at construction.
/// Bundles services, shared collections, and Func callbacks so the sub-VM can reload
/// parent state after a successful transaction without holding a back-reference to
/// <see cref="Portfolio.PortfolioViewModel"/>.
/// </summary>
internal sealed record TransactionDialogDependencies(
    // Services
    ITransactionWorkflowService TransactionWorkflow,
    ITradeDeletionWorkflowService TradeDeletion,
    ITradeMetadataWorkflowService TradeMetadata,
    ILoanMutationWorkflowService LoanMutation,
    ICreditCardTransactionWorkflowService CreditCardTransaction,
    IStockSearchService Search,
    PortfolioTradeDialogController TradeDialogController,
    IAccountUpsertWorkflowService? AccountUpsert,
    ISnackbarService? Snackbar,
    // Shared parent collections — read-only views; parent owns mutation.
    ReadOnlyObservableCollection<TradeRowViewModel> Trades,
    ReadOnlyObservableCollection<PortfolioRowViewModel> Positions,
    ReadOnlyObservableCollection<CashAccountRowViewModel> CashAccounts,
    ReadOnlyObservableCollection<LiabilityRowViewModel> Liabilities,
    // Sub-VM references for cross-dialog coordination
    AddAssetDialogViewModel AddAssetDialog,
    SellPanelViewModel SellPanel,
    // Callbacks for parent-side side-effects after TX operations
    Func<CashAccountRowViewModel?> GetDefaultCashAccount,
    Func<LiabilityRowViewModel, Task> LoadLoanScheduleAsync,
    Func<Task> LoadLiabilitiesAsync,
    Func<Task> LoadPositionsAsync,
    Func<Task> LoadTradesAsync,
    Func<Task> ReloadAccountBalancesAsync,
    Action RebuildTotals,
    Func<string, string, string> Localize,
    // P1 收支管理：收支分類與自動分類規則來源（皆可為 null 以保留向後相容）
    ICategoryRepository? CategoryRepository = null,
    IAutoCategorizationRuleRepository? AutoCategorizationRuleRepository = null,
    // L1 perf: post-edit cleanup needs positions + trades + balances + totals,
    // but calling each delegate triggered three separate _loadService.LoadAsync
    // round-trips. ReloadAllAsync runs one load and applies every slice in
    // sequence — used by ConfirmTx's edit-cleanup branch only. Optional with a
    // null-default so existing test factories without the delegate still build.
    Func<Task>? ReloadAllAsync = null,
    // Portfolio-Groups-Refactor P3 — 群組目錄。null 時 dialog 隱藏 group ComboBox。
    PortfolioGroupCatalog? GroupCatalog = null,
    // 預設手續費折扣讀取 callback（由 AppSettings.DefaultCommissionDiscount 帶）。
    // 用 Func 避免直接持 IAppSettingsService 介面參考，跟其他 callback 統一風格。
    Func<decimal>? GetDefaultCommissionDiscount = null,
    // P2.2 — 支援幣別清單（由 ICurrencyService.SupportedCurrencies 帶）。
    // 跟 Settings 頁共用清單，避免不同 dropdown 看到不同的幣別子集。
    Func<IReadOnlyList<string>>? GetSupportedCurrencies = null,
    // P2.5 — 「+ 新增資產」CTA 的進入點。使用者點下去 → 關 dialog → 開「新增投資」流程
    // (PortfolioViewModel.OpenAddWatchlistDialogCommand)。null 時 sentinel 不渲染。
    Action? OpenAddNewAsset = null,
    // P2.6 — 「最近使用的資產」 LRU 清單存取。Get 給 BuildAvailableAssets 讀，
    // Record 在使用者確認交易後寫（PortfolioViewModel 接到 AppSettings.RecentlyUsedAssetIds）。
    // null 時最近使用分組不會出現。
    Func<IReadOnlyList<Guid>>? GetRecentAssetIds = null,
    Action<Guid>? RecordRecentAsset = null,
    // Transaction FX settlement — optional so tests and lightweight hosts can omit it.
    TransactionFxRateResolver? TransactionFxRateResolver = null);

/// <summary>
/// 交易類型 ComboBox 的一個選項。Key 對齊 <see cref="TransactionDialogViewModel.TxType"/>
/// 的字串字面，DisplayKey / GroupKey 是 DynamicResource 鍵 — XAML 透過
/// ResourceKeyToStringConverter 解析成當下語系的字串。
/// </summary>
public sealed record TradeTypeOption(string Key, string DisplayKey, string GroupKey);

/// <summary>
/// Owns all transaction-dialog observable state, validation, and commands.
/// After a successful record/delete, raises <see cref="TransactionCompleted"/> or
/// <see cref="TradeDeleted"/> so <see cref="Portfolio.PortfolioViewModel"/> can
/// reload positions, trades, balances, and totals.
/// </summary>
public partial class TransactionDialogViewModel : ObservableObject  // public so PortfolioDependencies (public record) can reference it
{
    private readonly ITransactionWorkflowService _transactionWorkflowService;
    private readonly ITradeDeletionWorkflowService _tradeDeletionWorkflowService;
    private readonly ITradeMetadataWorkflowService _tradeMetadataWorkflowService;
    private readonly ILoanMutationWorkflowService _loanMutationWorkflowService;
    private readonly ICreditCardTransactionWorkflowService _creditCardTransactionWorkflowService;
    private readonly IStockSearchService _search;
    private readonly PortfolioTradeDialogController _tradeDialogController;
    private readonly IAccountUpsertWorkflowService? _accountUpsert;
    private readonly ISnackbarService? _snackbar;
    private readonly ICategoryRepository? _categoryRepository;
    private readonly IAutoCategorizationRuleRepository? _ruleRepository;

    /// <summary>Portfolio-Groups-Refactor P3 — 共用群組目錄（從 DI 注入），可為 null。</summary>
    public PortfolioGroupCatalog? GroupCatalog { get; private set; }

    /// <summary>
    /// True 當啟用群組功能（catalog 非 null 且至少一個 user-created group）— XAML 由此
    /// 決定 ComboBox 可見性。P3.9 — 排除 IsSystem default group (見
    /// PortfolioViewModel.HasPortfolioGroups 的完整理由)。
    /// </summary>
    public bool IsGroupSelectorVisible => GroupCatalog?.Groups.Any(g => !g.IsSystem) == true;

    /// <summary>使用者在 trade dialog 內選定的群組。null = 沿用 PortfolioGroup.DefaultId。</summary>
    [ObservableProperty]
    private PortfolioGroup? _selectedPortfolioGroup;
    private IReadOnlyList<AutoCategorizationRule> _autoRulesCache = Array.Empty<AutoCategorizationRule>();

    // Shared parent collections exposed as forwarding properties so TxForm XAML
    // (whose DataContext becomes this VM) can still bind to CashAccounts, Positions, etc.
    public ReadOnlyObservableCollection<TradeRowViewModel> Trades { get; }
    public ReadOnlyObservableCollection<PortfolioRowViewModel> Positions { get; }
    public ReadOnlyObservableCollection<CashAccountRowViewModel> CashAccounts { get; }
    public ReadOnlyObservableCollection<LiabilityRowViewModel> Liabilities { get; }

    // Sub-VM references forwarded so BuyTxForm can bind to AddAssetDialog.*
    // and SellTxForm can bind to SellPanel.*
    public AddAssetDialogViewModel AddAssetDialog { get; }
    public SellPanelViewModel SellPanel { get; }

    // Callbacks for parent-side reload operations
    private readonly Func<CashAccountRowViewModel?> _getDefaultCashAccount;
    private readonly Func<LiabilityRowViewModel, Task> _loadLoanScheduleAsync;
    private readonly Func<Task> _loadLiabilitiesAsync;
    private readonly Func<Task> _loadPositionsAsync;
    private readonly Func<Task> _loadTradesAsync;
    private readonly Func<Task> _reloadAccountBalancesAsync;
    private readonly Func<Task>? _reloadAllAsync;
    private readonly Func<decimal>? _getDefaultCommissionDiscount;
    private readonly Func<IReadOnlyList<string>>? _getSupportedCurrencies;
    private readonly Action? _openAddNewAsset;
    private readonly Func<IReadOnlyList<Guid>>? _getRecentAssetIds;
    private readonly Action<Guid>? _recordRecentAsset;
    private readonly TransactionFxRateResolver? _transactionFxRateResolver;
    private bool _suppressBuyFxManualTracking;
    // P5.8b prereq — Sell + Dividend mirror Buy's suppress flag for programmatic
    // FxRate writes (EditTrade restore + SetResolvedXxxFx). Without this the
    // FxRate PropertyChanged handler would falsely flip IsFxManual=true and
    // overwrite FxSourceLabel = "manual" during automated fills.
    private bool _suppressSellFxManualTracking;
    private bool _suppressDividendFxManualTracking;
    // Total-mode 防回寫迴圈：成交總額 → 單價(AddPrice) 同步時設旗標，讓單價變動的「反推回總額」
    // 寫回路徑跳過，否則 TotalCost↔AddPrice 互寫 + 千分位行為 + 每鍵觸發會把使用者輸入改掉/放大。
    /// <summary>寫入 AddPrice 期間暫停「AddPrice → TotalCost」回寫，避免雙向連動打架。</summary>
    private bool _suppressBuyTotalRewrite;

    /// <summary>寫入 TotalCost 期間暫停「TotalCost → AddPrice」回寫（上者的反向）。</summary>
    private bool _suppressBuyPriceRewrite;
    private readonly Action _rebuildTotals;
    private readonly Func<string, string, string> _localize;

    /// <summary>
    /// Raised after a successful transaction (income, dividend, cash-flow, loan, transfer, buy, sell).
    /// The parent VM reloads positions, trades, balances, and totals in response.
    /// </summary>
    public event EventHandler? TransactionCompleted;

    /// <summary>
    /// Raised after a successful trade deletion (from the edit dialog's Delete button).
    /// The parent VM reloads positions, trades, balances, and totals in response.
    /// </summary>
    public event EventHandler? TradeDeleted;

    internal TransactionDialogViewModel(TransactionDialogDependencies deps)
    {
        ArgumentNullException.ThrowIfNull(deps);

        // H1 — react to Buy/Sell/Div/Loan/Transfer/CreditCard.X changes.
        Buy.PropertyChanged += OnBuyPriceModeChanged;
        Sell.PropertyChanged += OnSellTxChanged;
        Div.PropertyChanged += OnDividendTxChanged;
        Loan.PropertyChanged += OnLoanTxChanged;
        Transfer.PropertyChanged += OnTransferTxChanged;
        CreditCard.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CreditCardTxViewModel.Card))
                NotifyImpactPreviewChanged();
        };

        // M6-B — read-only views of the internally-mutated suggestion collections.
        CashAccountSuggestions = new ReadOnlyObservableCollection<string>(_cashAccountSuggestions);
        PositionSuggestions = new ReadOnlyObservableCollection<PositionSuggestion>(_positionSuggestions);

        _transactionWorkflowService = deps.TransactionWorkflow;
        _tradeDeletionWorkflowService = deps.TradeDeletion;
        _tradeMetadataWorkflowService = deps.TradeMetadata;
        _loanMutationWorkflowService = deps.LoanMutation;
        _creditCardTransactionWorkflowService = deps.CreditCardTransaction;
        _search = deps.Search;
        _tradeDialogController = deps.TradeDialogController;
        _accountUpsert = deps.AccountUpsert;
        _snackbar = deps.Snackbar;

        Trades = deps.Trades;
        Positions = deps.Positions;
        CashAccounts = deps.CashAccounts;
        Liabilities = deps.Liabilities;

        AddAssetDialog = deps.AddAssetDialog;
        SellPanel = deps.SellPanel;

        // The Buy form's price / quantity / total-cost fields live on the
        // AddAssetDialog sub-VM. Surface their changes to the impact preview
        // via PropertyChanged subscription so the preview banner re-projects
        // as the user types — mirroring how UpdateBuyPreview already wires.
        AddAssetDialog.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(AddAssetDialog.AddPrice)
                              or nameof(AddAssetDialog.AddQuantity)
                              or nameof(AddAssetDialog.AddCost))
            {
                OnPropertyChanged(nameof(TxBuyComputedTotalDisplay));
                OnPropertyChanged(nameof(TxBuyComputedUnitPriceDisplay));
                NotifyImpactPreviewChanged();

                // 成交明細三欄連動（數量 / 每股價格 / 成交總額）：使用者打哪一欄，哪一欄就是權威，
                // 另一欄自動補算。不再有「單價/總額」模式切換 — PriceMode 降級為內部隱含旗標，
                // 只記錄「金額類欄位最後是誰被使用者輸入」，供確認層沿用既有 GROSS 語意。
                // _suppressBuyTotalRewrite 為 true 代表這次變動是 TotalCost→AddPrice 的程式回寫，
                // 不是使用者輸入，不可反過來再改權威。
                if (!_suppressBuyTotalRewrite &&
                    e.PropertyName is nameof(AddAssetDialog.AddPrice) or nameof(AddAssetDialog.AddQuantity))
                {
                    // 使用者親手打「每股價格」→ 單價為權威。改「數量」不改變權威（沿用目前這邊）。
                    if (e.PropertyName is nameof(AddAssetDialog.AddPrice))
                        Buy.PriceMode = "unit";

                    RecomputeBuyDerivedAmount();
                }

                SyncBuyGrossNative();
            }
            // P3 — keep Buy.InstrumentCurrency in sync with the selected symbol's
            // currency (filled by SelectSuggestion after the user picks an autocomplete
            // entry, or via AddExchange → registry lookup as fallback). Buy.IsCrossCurrency
            // depends on this and drives the FX-rate field visibility in BuyTxForm.xaml.
            if (e.PropertyName is nameof(AddAssetDialog.AddSymbolCurrency)
                              or nameof(AddAssetDialog.AddExchange))
            {
                Buy.InstrumentCurrency = ResolveCurrentInstrumentCurrency();
            }
        };

        _getDefaultCashAccount = deps.GetDefaultCashAccount;
        _loadLoanScheduleAsync = deps.LoadLoanScheduleAsync;
        _loadLiabilitiesAsync = deps.LoadLiabilitiesAsync;
        _loadPositionsAsync = deps.LoadPositionsAsync;
        _loadTradesAsync = deps.LoadTradesAsync;
        _reloadAccountBalancesAsync = deps.ReloadAccountBalancesAsync;
        _reloadAllAsync = deps.ReloadAllAsync;
        _rebuildTotals = deps.RebuildTotals;
        _localize = deps.Localize;
        _categoryRepository = deps.CategoryRepository;
        _ruleRepository = deps.AutoCategorizationRuleRepository;
        GroupCatalog = deps.GroupCatalog;
        _getDefaultCommissionDiscount = deps.GetDefaultCommissionDiscount;
        _getSupportedCurrencies = deps.GetSupportedCurrencies;
        _openAddNewAsset = deps.OpenAddNewAsset;
        _getRecentAssetIds = deps.GetRecentAssetIds;
        _recordRecentAsset = deps.RecordRecentAsset;
        _transactionFxRateResolver = deps.TransactionFxRateResolver;

        ExpenseCategories = new ReadOnlyObservableCollection<CategoryRowViewModel>(_expenseCategories);
        IncomeCategories = new ReadOnlyObservableCollection<CategoryRowViewModel>(_incomeCategories);

        // 啟動時載入分類與自動規則快照（失敗不影響其它功能）
        if (_categoryRepository is not null || _ruleRepository is not null)
            _ = LoadCategoriesAsync();
    }

    // ── P1 收支管理：分類下拉與自動分類 → 見 TransactionDialogViewModel.Categories.cs ──

    // ── Transaction Dialog state ──────────────────────────────────────────────────────

    // 新增紀錄 Dialog
    [ObservableProperty] private bool _isTxDialogOpen;
    [ObservableProperty] private string _txType = "income";

    /// <summary>
    /// 12 種 trade type 完整清單 — P2.3 之前 AvailableTradeTypes 直接回這份；P2.3 起依
    /// SelectedAsset.Kind 過濾出子集。Key 跟 <see cref="TxType"/> 字面對齊。
    /// </summary>
    private static readonly IReadOnlyList<TradeTypeOption> _allTradeTypes = new TradeTypeOption[]
    {
        new("buy",                "Portfolio.Tx.Buy",                  "Portfolio.Record.Group.Investment"),
        new("sell",               "Portfolio.Tx.Sell",                 "Portfolio.Record.Group.Investment"),
        new("cashDiv",            "Portfolio.Tx.CashDiv",              "Portfolio.Record.Group.Investment"),
        new("stockDiv",           "Portfolio.Tx.StockDiv",             "Portfolio.Record.Group.Investment"),
        new("income",             "Portfolio.Tx.Income",               "Portfolio.Record.Group.IncomeExpense"),
        new("deposit",            "Portfolio.TradeType.Deposit",       "Portfolio.Record.Group.Cash"),
        new("withdrawal",         "Portfolio.Tx.WithdrawalAction",     "Portfolio.Record.Group.Cash"),
        new("transfer",           "Portfolio.Tx.TransferAction",       "Portfolio.Record.Group.Cash"),
        new("loanBorrow",         "Portfolio.TradeType.LoanBorrow",    "Portfolio.Record.Group.Liability"),
        new("loanRepay",          "Portfolio.TradeType.LoanRepay",     "Portfolio.Record.Group.Liability"),
        new("creditCardCharge",   "Portfolio.TradeType.CreditCardCharge",  "Portfolio.Record.Group.Liability"),
        new("creditCardPayment",  "Portfolio.TradeType.CreditCardPayment", "Portfolio.Record.Group.Liability"),
    };

    /// <summary>
    /// P2.3 — asset-aware type 過濾。「交易類型」ComboBox 的可選清單，依目前
    /// <see cref="SelectedAsset"/> 的 Kind 過濾。資產為 null 時回空集合，搭配
    /// <see cref="CanSelectTxType"/> 把 ComboBox disable 起來。
    /// </summary>
    public IReadOnlyList<TradeTypeOption> AvailableTradeTypes
    {
        get
        {
            var allowed = ResolveAvailableTypeKeys(SelectedAsset);
            if (allowed.Count == 0)
                return Array.Empty<TradeTypeOption>();
            return _allTradeTypes.Where(o => allowed.Contains(o.Key)).ToList();
        }
    }

    /// <summary>
    /// 類型 chips 的主選項 — <see cref="AvailableTradeTypes"/> 的前兩項（依 source 順序）。
    /// 投資標的前兩項即 buy/sell，故股票/ETF 行為不變；現金/負債等情境則各自取自己的前兩項
    /// （如 income/deposit），避免主 chip 列為空只剩「更多」。AddRecordDialog 用它渲染主 chip 列。
    /// </summary>
    public IReadOnlyList<TradeTypeOption> PrimaryTradeTypes =>
        AvailableTradeTypes.Take(2).ToList();

    /// <summary>
    /// 類型 chips 的「更多」選項 — <see cref="AvailableTradeTypes"/> 前兩項以外其餘項
    /// （保持原順序），走「更多 ▾」popup。
    /// </summary>
    public IReadOnlyList<TradeTypeOption> MoreTradeTypes =>
        AvailableTradeTypes.Skip(2).ToList();

    /// <summary>True 時顯示「更多 ▾」按鈕（<see cref="MoreTradeTypes"/> 非空）。</summary>
    public bool HasMoreTradeTypes => MoreTradeTypes.Count > 0;

    /// <summary>「更多 ▾」popup 開闔狀態；選定任一類型後由 <see cref="SelectTxType"/> 關閉。</summary>
    [ObservableProperty] private bool _isMoreTypesPopupOpen;

    /// <summary>
    /// 由類型 chip / 「更多」popup 列點擊呼叫 — 設定 <see cref="TxType"/> 並關閉「更多」popup。
    /// 對齊原 type ComboBox SelectedValue TwoWay 的行為（設 TxType 即觸發 OnTxTypeChanged）。
    /// </summary>
    [RelayCommand]
    private void SelectTxType(string? key)
    {
        if (!CanSelectTxType || string.IsNullOrEmpty(key))
            return;
        TxType = key;
        IsMoreTypesPopupOpen = false;
    }

    /// <summary>
    /// True 才允許使用者開「交易類型」下拉。SelectedAsset = null 時 disabled。
    /// </summary>
    public bool CanSelectTxType => SelectedAsset is not null;

    /// <summary>給 type ComboBox 用的 tooltip key — disabled 時提示「請先選擇資產」。</summary>
    public string TxTypePickerHintKey => CanSelectTxType
        ? "Portfolio.Tx.TypePicker.Hint"
        : "Portfolio.Tx.TypePicker.NeedAsset";

    /// <summary>
    /// Asset-Kind → 可用 trade type key 對應表。改 mapping 時記得同步
    /// docs/planning/AddRecordDialog-Phase2-AssetFirst.md 的 Type-Asset 相容性矩陣。
    /// </summary>
    private static IReadOnlySet<string> ResolveAvailableTypeKeys(TxAssetSubject? asset)
    {
        if (asset is null)
            return new HashSet<string>(StringComparer.Ordinal);
        return asset.Kind switch
        {
            TxAssetKind.Stock => new HashSet<string>(StringComparer.Ordinal)
                { "buy", "sell", "cashDiv", "stockDiv" },
            TxAssetKind.Fund => new HashSet<string>(StringComparer.Ordinal)
                { "buy", "sell", "cashDiv" },
            TxAssetKind.Crypto => new HashSet<string>(StringComparer.Ordinal)
                { "buy", "sell" },
            TxAssetKind.Metal => new HashSet<string>(StringComparer.Ordinal)
                { "buy", "sell" },
            TxAssetKind.Bond => new HashSet<string>(StringComparer.Ordinal)
                { "buy", "sell", "cashDiv" },  // 票息走 cashDiv
            TxAssetKind.CashAccount => new HashSet<string>(StringComparer.Ordinal)
                { "income", "deposit", "withdrawal", "transfer" },
            TxAssetKind.Liability => new HashSet<string>(StringComparer.Ordinal)
                { "loanBorrow", "loanRepay", "creditCardCharge", "creditCardPayment" },
            _ => new HashSet<string>(StringComparer.Ordinal),
        };
    }

    /// <summary>Non-null when editing an existing trade (vs. creating new).</summary>
    [ObservableProperty] private Guid? _editingTradeId;
    [ObservableProperty] private bool _isRevisionMode;
    [ObservableProperty] private bool _isRevisionReplacePromptOpen;
    [ObservableProperty] private string _revisionReplacePromptError = string.Empty;
    [ObservableProperty] private bool _isDeleteConfirmOpen;
    [ObservableProperty] private string _deleteTargetName = string.Empty;
    [ObservableProperty] private Guid? _txLoanScheduleEntryId;
    private Guid? _revisionSourceTradeId;
    private bool _preserveRevisionSourceOnClose;

    // Q02 — the trade row currently being edited, kept so BuildAvailableAssets can inject a
    // synthesized subject for an asset that's no longer in the live Positions/CashAccounts/
    // Liabilities view (e.g. a CLOSED lot excluded by ShowClosedPositions, off by default). The asset picker
    // ComboBox renders blank unless SelectedItem ∈ ItemsSource, so the synthesized subject must
    // be part of AvailableAssets — not just assigned to SelectedAsset. Edit-only: OpenTxDialog
    // clears it so the create path never carries a synthesized entry and the next open is clean.
    private TradeRowViewModel? _editingTradeRow;

    // Phase 2.1 — asset selector state. P2.2 adds the cascade below.
    [ObservableProperty] private TxAssetSubject? _selectedAsset;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowAssetSelector))]
    [NotifyPropertyChangedFor(nameof(ShowInvestmentPositionPicker))]
    [NotifyPropertyChangedFor(nameof(ShowDividendPositionPicker))]
    private bool _isAssetContextLocked;

    public bool ShowAssetSelector => !ShowEditLockedSummary && !IsAssetContextLocked;
    public bool ShowInvestmentPositionPicker => !IsAssetContextLocked;
    public bool ShowDividendPositionPicker => ShowInvestmentPositionPicker;

    // Phase 2.2 — currency dropdown is now a first-class field. When a user picks an
    // asset, OnSelectedAssetChanged copies asset.Currency here unless the user has
    // already manually changed the currency (tracked by _userTouchedCurrency).
    // The dirty flag resets on each fresh dialog open so the next session starts clean.
    [ObservableProperty] private string _txCurrency = "TWD";
    private bool _userTouchedCurrency;
    private bool _suppressCurrencyDirty;  // set when we (the VM) write TxCurrency programmatically

    public bool IsTxCurrencyEditable =>
        SelectedAsset is null || !IsInvestmentAssetKind(SelectedAsset.Kind);

    /// <summary>
    /// 成交明細三欄（數量 / 每股價格 / 成交總額）的連動核心：依 <c>Buy.PriceMode</c>
    /// （＝使用者最後輸入的金額欄）把「非權威」的那一欄重算出來。
    ///
    /// <para>單價為權威 → 總額 = 單價 × 數量；總額為權威 → 單價 = 總額 ÷ 數量。
    /// 兩個方向各自用對向的 suppress 旗標擋住回授，否則會互相覆蓋（千分位 + 每鍵觸發會放大成亂跳）。</para>
    ///
    /// <para>反推單價刻意用高精度（非 F4）：否則 單價(4位) × 數量 會把使用者輸入的總額 round-trip 掉
    /// （例：123,456,789 → 6172.8395 → 123,456,790）。顯示用的格式化另由
    /// <see cref="TxBuyComputedUnitPriceDisplay"/> 負責，不受此精度影響。</para>
    /// </summary>
    private void RecomputeBuyDerivedAmount()
    {
        if (!ParseHelpers.TryParseInt(AddAssetDialog.AddQuantity, out var qty) || qty <= 0)
            return;

        if (Buy.IsTotalMode)
        {
            if (ParseHelpers.TryParseDecimal(Buy.TotalCost, out var total) && total > 0)
            {
                _suppressBuyTotalRewrite = true;
                try { AddAssetDialog.AddPrice = (total / qty).ToString("0.##########"); }
                finally { _suppressBuyTotalRewrite = false; }
            }
            return;
        }

        if (ParseHelpers.TryParseDecimal(AddAssetDialog.AddPrice, out var price) && price > 0)
        {
            _suppressBuyPriceRewrite = true;
            try { Buy.TotalCost = (price * qty).ToString("0.##"); }
            finally { _suppressBuyPriceRewrite = false; }
        }
    }

    /// <summary>
    /// 把「成交價金」（數量 × 每股價格，成交幣別）同步進 <c>Buy</c> 供溢價合理性檢查用。
    /// 總額模式下改用使用者輸入的 <c>Buy.TotalCost</c>（那就是價金本身）。
    /// 任一欄缺漏／非正數 → null＝資料不足，Buy 端不顯示檢查。純顯示用途，不碰寫入路徑。
    /// </summary>
    private void SyncBuyGrossNative()
    {
        if (Buy.IsTotalMode)
        {
            Buy.GrossNative = ParseHelpers.TryParseDecimal(Buy.TotalCost, out var total) && total > 0m
                ? total
                : null;
            return;
        }

        Buy.GrossNative =
            ParseHelpers.TryParseDecimal(AddAssetDialog.AddPrice, out var price) && price > 0m &&
            ParseHelpers.TryParseInt(AddAssetDialog.AddQuantity, out var qty) && qty > 0
                ? price * qty
                : null;
    }

    /// <summary>
    /// 交易幣別列僅在「幣別可編輯」（現金/負債/無資產）且非 meta-only 編輯時顯示。
    /// 投資標的幣別由所選資產決定、已顯示於資產列（SecondaryLine 的「· CCY」），故隱藏這個
    /// 原本唯讀又長得像可選的下拉，消除「說由資產決定卻擺個下拉」的矛盾。
    /// </summary>
    public bool ShowTxCurrencyRow => IsTxCurrencyEditable && !ShowEditLockedSummary;

    partial void OnTxCurrencyChanged(string value)
    {
        if (!_suppressCurrencyDirty &&
            TryGetSelectedInvestmentCurrency(out var selectedCurrency) &&
            !string.Equals(value, selectedCurrency, StringComparison.OrdinalIgnoreCase))
        {
            _suppressCurrencyDirty = true;
            try
            { TxCurrency = selectedCurrency; }
            finally { _suppressCurrencyDirty = false; }
            return;
        }

        if (!_suppressCurrencyDirty)
            _userTouchedCurrency = true;

        // Buy.InstrumentCurrency is the selected instrument's currency, not the
        // cash-account debit currency. Only use TxCurrency as a fallback before an
        // investment asset/symbol has established its own currency.
        if (SelectedAsset is null &&
            string.IsNullOrWhiteSpace(Buy.InstrumentCurrency) &&
            !string.IsNullOrWhiteSpace(value))
        {
            Buy.InstrumentCurrency = value.Trim().ToUpperInvariant();
        }
    }

    private bool TryGetSelectedInvestmentCurrency(out string currency)
    {
        currency = string.Empty;
        if (SelectedAsset is null ||
            !IsInvestmentAssetKind(SelectedAsset.Kind) ||
            string.IsNullOrWhiteSpace(SelectedAsset.Currency))
        {
            return false;
        }

        currency = SelectedAsset.Currency.Trim().ToUpperInvariant();
        return true;
    }

    private static bool IsInvestmentAssetKind(TxAssetKind kind) =>
        kind is TxAssetKind.Stock or TxAssetKind.Fund or TxAssetKind.Crypto or TxAssetKind.Metal or TxAssetKind.Bond;


    /// <summary>
    /// 供 XAML ComboBox 用的支援幣別清單。優先用 DI 注入的 callback (跟 Settings 共用)；
    /// 沒注入時 fallback 到 (asset 出現過的幣別 ∪ TWD/USD)，至少不會空清單。
    /// </summary>
    public IReadOnlyList<string> SupportedCurrencies
    {
        get
        {
            var fromService = _getSupportedCurrencies?.Invoke();
            if (fromService is { Count: > 0 })
                return fromService;
            // Fallback：從目前 assets 看到的幣別 union 出來 + TWD/USD 保底
            var set = new System.Collections.Generic.SortedSet<string>(StringComparer.OrdinalIgnoreCase) { "TWD", "USD" };
            foreach (var a in AvailableAssets)
                if (!string.IsNullOrWhiteSpace(a.Currency))
                    set.Add(a.Currency.ToUpperInvariant());
            return set.ToList();
        }
    }

    /// <summary>
    /// P2.2 cascade — 使用者選了資產後自動帶幣別 + 推薦現金帳戶。
    /// 守則：
    /// <list type="bullet">
    ///   <item>selected = null（清空）→ 不動其他欄位（避免清掉使用者已填的值）</item>
    ///   <item>使用者已手動改過 TxCurrency → 不覆蓋（_userTouchedCurrency=true）</item>
    ///   <item>SuggestedCashAccountId 找得到對應的 row → 設 TxCashAccount；找不到不動</item>
    /// </list>
    /// </summary>
    partial void OnSelectedAssetChanged(TxAssetSubject? value)
    {
        // P2.3 — 不管 value 是否 null，都要通知 AvailableTradeTypes / CanSelectTxType 重新計算。
        OnPropertyChanged(nameof(AvailableTradeTypes));
        OnPropertyChanged(nameof(PrimaryTradeTypes));
        OnPropertyChanged(nameof(MoreTradeTypes));
        OnPropertyChanged(nameof(HasMoreTradeTypes));
        OnPropertyChanged(nameof(CanSelectTxType));
        OnPropertyChanged(nameof(TxTypePickerHintKey));
        OnPropertyChanged(nameof(IsTxCurrencyEditable));
        OnPropertyChanged(nameof(ShowTxCurrencyRow));

        if (value is null)
            return;

        // P2.5 — 「+ 新增資產」 sentinel 被選到 → 重置 SelectedAsset 並觸發新增資產流程。
        // 重置必須先做，避免下次再選到同一個 sentinel 不會 raise PropertyChanged。
        if (value.Id == AddNewAssetSentinelId)
        {
            SelectedAsset = null;
            _openAddNewAsset?.Invoke();
            return;
        }

        // 1. 幣別 — 投資標的的成交幣別由資產決定；非投資資產仍保留舊的使用者覆寫規則。
        if ((IsInvestmentAssetKind(value.Kind) || !_userTouchedCurrency) &&
            !string.IsNullOrWhiteSpace(value.Currency))
        {
            _suppressCurrencyDirty = true;
            try
            { TxCurrency = value.Currency.ToUpperInvariant(); }
            finally { _suppressCurrencyDirty = false; }
        }

        // 2. 現金帳戶 — 先用資產直接指定的 SuggestedCashAccountId，
        //    沒指定時找跟 TxCurrency 同幣別的第一個 CashAccount。
        Guid? cashId = value.SuggestedCashAccountId
            ?? CashAccounts.FirstOrDefault(c =>
                string.Equals(c.Currency, TxCurrency, StringComparison.OrdinalIgnoreCase))?.Id;
        if (cashId is { } id)
            TxCashAccount = CashAccounts.FirstOrDefault(c => c.Id == id);

        // 3. P2.3 — 若 TxType 不在新 asset 的允許清單裡，自動切到第一個合法 type。
        //    例：使用者剛剛在 stock 上選了「現金股利」，現在改選 cash account → 自動跳到「收入」。
        var allowed = ResolveAvailableTypeKeys(value);
        if (allowed.Count > 0 && !allowed.Contains(TxType))
        {
            TxType = AvailableTradeTypes[0].Key;
        }

        // 4. P2.5 — 把選的資產 sync 進 Buy / AddAssetDialog 內部欄位，讓下游 ConfirmBuyAsync
        //    照舊運作但 BuyTxForm 不必再暴露 AssetType ComboBox + Symbol 輸入框。
        SyncSelectedAssetIntoBuyState(value);
        SyncSelectedAssetIntoPositionState(value);
        SyncSelectedAssetIntoLiabilityState(value);
    }

    /// <summary>
    /// P2.5 — 把 SelectedAsset 的 Kind / Symbol 灌進 Buy.AssetType + AddAssetDialog.AddSymbol，
    /// 讓 ConfirmBuyAsync 沿用舊的內部欄位邏輯，但 UI 上不必再重複輸入。
    /// </summary>
    private void SyncSelectedAssetIntoBuyState(TxAssetSubject? asset)
    {
        if (asset is null)
            return;

        // Kind → Buy.AssetType (string "stock" / "fund" / "metal" / "bond" / "crypto")
        var nextAssetType = asset.Kind switch
        {
            TxAssetKind.Stock => "stock",
            TxAssetKind.Fund => "fund",
            TxAssetKind.Metal => "metal",
            TxAssetKind.Bond => "bond",
            TxAssetKind.Crypto => "crypto",
            _ => Buy.AssetType,  // 非投資資產維持現值（不會走 Buy 流程）
        };
        if (Buy.AssetType != nextAssetType)
            Buy.AssetType = nextAssetType;

        if (!string.IsNullOrWhiteSpace(asset.Currency))
        {
            var instrumentCurrency = asset.Currency.Trim().ToUpperInvariant();
            Buy.InstrumentCurrency = instrumentCurrency;
            AddAssetDialog.AddSymbolCurrency = instrumentCurrency;
        }

        // Symbol → AddAssetDialog.AddSymbol。Stock / Fund / Bond 等才有 Symbol；
        // crypto / metal 不一定有；CashAccount / Liability 一定 null。
        if (!string.IsNullOrWhiteSpace(asset.Symbol)
            && !string.Equals(AddAssetDialog.AddSymbol, asset.Symbol, StringComparison.OrdinalIgnoreCase))
        {
            AddAssetDialog.SuppressSuggestions = true;
            try
            { AddAssetDialog.AddSymbol = asset.Symbol; }
            finally { AddAssetDialog.SuppressSuggestions = false; AddAssetDialog.IsSuggestionsOpen = false; }
        }
    }

    /// <summary>
    /// 投資類資產選好後，把對應持倉灌進 <see cref="Sell"/>.Position 與 <see cref="Div"/>.Position，
    /// 讓「賣出」「現金股利」表單不必再用「股票」下拉重複選一次（與「買入」對齊）。
    /// 每個投資持倉在統一選擇器各對應一個 subject（subject.Id == position.Id），故可直接 by-Id 對應。
    /// 非投資類（現金 / 負債）不動，對應表單本來就不顯示持倉選擇器。
    /// </summary>
    private void SyncSelectedAssetIntoPositionState(TxAssetSubject? asset)
    {
        if (asset is null || !IsInvestmentAssetKind(asset.Kind))
            return;
        var position = Positions.FirstOrDefault(p => p.Id == asset.Id);
        if (position is null)
            return;
        Sell.Position = position;
        Div.Position = position;          // 現金股利
        Div.StockPosition = position;     // 股票股利
    }

    /// <summary>
    /// 負債類資產選好後，把對應信用卡灌進 <see cref="CreditCard"/>.Card，
    /// 讓信用卡消費/還款表單不必再用「信用卡」下拉重複選一次（與 OpenTxDialogForLiability 一致）。
    /// 貸款（Loan.Label）另由 <see cref="SyncSelectedAssetIntoLoanState"/> 處理。
    /// </summary>
    private void SyncSelectedAssetIntoLiabilityState(TxAssetSubject? asset)
    {
        if (asset is null || asset.Kind != TxAssetKind.Liability)
            return;
        // 信用卡：CreditCardOptions 只含信用卡負債；subject.Id 對應負債的 AssetId（見 1.3 subject building）。
        // legacy 信用卡 (AssetId=null → subject Id=Guid.Empty) 不自動對應，屬罕見舊資料。
        var card = CreditCardOptions.FirstOrDefault(c => c.AssetId == asset.Id);
        if (card is not null)
        {
            CreditCard.Card = card;
            return;
        }
        // 貸款：把所選貸款負債的名稱帶進 Loan.Label，讓「貸款名稱」反映上方「選擇資產」並觸發
        // 還款自動帶入。ConfirmLoanAsync 以名稱識別貸款（可建新），故仍保留可編輯、不隱藏。
        var loan = Liabilities.FirstOrDefault(l => l.AssetId == asset.Id && l.IsLoan);
        if (loan is not null)
            Loan.Label = loan.Label;
    }

    /// <summary>
    /// 統一資產選擇器的 item 來源 — 動態組合 Positions / CashAccounts / Liabilities 三類。
    /// 每次 dialog 開啟（OpenTxDialog / EditTrade）會 invalidate cache 重建；中間多次存取
    /// 會走 cache 避免 ComboBox 重新展開時 list reference 變動觸發 ICollectionView 重建。
    /// </summary>
    private IReadOnlyList<TxAssetSubject>? _cachedAvailableAssets;
    public IReadOnlyList<TxAssetSubject> AvailableAssets =>
        _cachedAvailableAssets ??= BuildAvailableAssets();

    /// <summary>
    /// 帶 group header 的 view，XAML ComboBox.ItemsSource 綁這個（不是 AvailableAssets）。
    /// GroupDescriptions 用 <see cref="TxAssetSubject.GroupKey"/>，XAML GroupStyle.HeaderTemplate
    /// 走 ResourceKeyToStringConverter 把 key 解析成「投資」/「現金」/「負債」。
    /// </summary>
    private System.ComponentModel.ICollectionView? _availableAssetsView;
    public System.ComponentModel.ICollectionView AvailableAssetsView
    {
        get
        {
            if (_availableAssetsView is null)
            {
                _availableAssetsView = System.Windows.Data.CollectionViewSource.GetDefaultView(AvailableAssets);
                _availableAssetsView.GroupDescriptions.Add(
                    new System.Windows.Data.PropertyGroupDescription(nameof(TxAssetSubject.GroupKey)));
                _availableAssetsView.Filter = AssetMatchesSearch;
            }
            return _availableAssetsView;
        }
    }

    /// <summary>
    /// P2.7 — 資產 picker 頂部搜尋框的過濾述詞。空字串 → 全部通過；非空 → 比對
    /// PrimaryName / SecondaryLine / Symbol（皆 case-insensitive substring）。「+ 新增資產」
    /// sentinel 永遠保留以避免使用者在搜尋無結果時卡住。搜尋時隱藏「最近使用」 group
    /// 避免同一檔資產在 Recent + Investment 兩處同時出現造成重複視覺噪音。
    /// </summary>
    private bool AssetMatchesSearch(object obj)
    {
        if (obj is not TxAssetSubject a)
            return false;
        if (a.Id == AddNewAssetSentinelId)
            return true;

        var q = AssetSearchText;
        if (string.IsNullOrWhiteSpace(q))
            return true;

        if (a.GroupKey == "Portfolio.Tx.Asset.Group.Recent")
            return false;

        return Contains(a.PrimaryName, q)
            || Contains(a.SecondaryLine, q)
            || Contains(a.Symbol, q);

        static bool Contains(string? source, string needle) =>
            !string.IsNullOrEmpty(source) &&
            source.Contains(needle, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// P2.7 — 頂部搜尋框雙向綁到這裡。VM 收到 keystroke 後 refresh view 觸發
    /// <see cref="AssetMatchesSearch"/> 重跑過濾。
    /// </summary>
    [ObservableProperty] private string _assetSearchText = string.Empty;

    partial void OnAssetSearchTextChanged(string value) => _availableAssetsView?.Refresh();

    private void InvalidateAvailableAssetsCache()
    {
        _cachedAvailableAssets = null;
        _availableAssetsView = null;
        OnPropertyChanged(nameof(AvailableAssets));
        OnPropertyChanged(nameof(AvailableAssetsView));
    }

    /// <summary>P2.5 — 「+ 新增資產」 sentinel 的識別 id（Guid.Empty 是 legacy liability，
    /// 用 NullGuid 跟 sentinel 衝突，這裡用一個 fixed GUID 哨兵）。</summary>
    public static readonly Guid AddNewAssetSentinelId = new("AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA");

    private IReadOnlyList<TxAssetSubject> BuildAvailableAssets()
    {
        // Step 1：先把三大類資產建到 `regular` 暫存清單。
        var regular = new List<TxAssetSubject>(Positions.Count + CashAccounts.Count + Liabilities.Count);

        // 1.1 投資持倉 (Stock / Fund / Crypto / Metal / Bond)
        //     PrimaryName = entity name；SecondaryLine = "(SYMBOL) · CCY"，無 symbol 就降為 "CCY"。
        //     SuggestedCashAccountId — 找跟此持倉幣別相符的第一個現金帳戶，給 P2.2 cascade 用。
        foreach (var p in Positions)
        {
            var ccy = string.IsNullOrWhiteSpace(p.Currency) ? "TWD" : p.Currency;
            var sameCcyCash = CashAccounts.FirstOrDefault(c =>
                string.Equals(c.Currency, ccy, StringComparison.OrdinalIgnoreCase));
            var secondary = string.IsNullOrWhiteSpace(p.Symbol)
                ? ccy
                : $"({p.Symbol}) · {ccy}";
            regular.Add(new TxAssetSubject(
                Kind: MapAssetType(p.AssetType),
                Id: p.Id,
                PrimaryName: p.Name,
                SecondaryLine: secondary,
                GroupKey: "Portfolio.Tx.Asset.Group.Investment",
                Currency: ccy,
                Symbol: p.Symbol,
                SuggestedCashAccountId: sameCcyCash?.Id));
        }
        // 1.2 現金帳戶 — SecondaryLine 「CASH · {ccy}」
        foreach (var c in CashAccounts)
        {
            regular.Add(new TxAssetSubject(
                Kind: TxAssetKind.CashAccount,
                Id: c.Id,
                PrimaryName: c.Name,
                SecondaryLine: $"CASH · {c.Currency}",
                GroupKey: "Portfolio.Tx.Asset.Group.Cash",
                Currency: c.Currency,
                SuggestedCashAccountId: c.Id));
        }
        // 1.3 負債 — SecondaryLine 「DEBT · {ccy}」。Legacy (AssetId=null) 仍列入，
        //     Id 用 Guid.Empty 哨兵，cascade 內可辨識為 fallback。
        foreach (var liab in Liabilities)
        {
            regular.Add(new TxAssetSubject(
                Kind: TxAssetKind.Liability,
                Id: liab.AssetId ?? Guid.Empty,
                PrimaryName: liab.Label,
                SecondaryLine: $"DEBT · {liab.BalanceAsMoney.Currency}",
                GroupKey: "Portfolio.Tx.Asset.Group.Liability",
                Currency: liab.BalanceAsMoney.Currency));
        }

        // Q02 — 編輯一筆標的已不在 live view 的交易（例：已平倉持倉被 ShowClosedPositions 預設關排除）時，
        // 合成該交易的 subject 併入 regular，否則 picker ComboBox 因 SelectedItem ∉ ItemsSource 而空白、
        // AvailableTradeTypes 也因 SelectedAsset.Kind 過濾不到而清空。已存在等價 subject（同 Kind+Id/Symbol）
        // 時優先用真的那筆，不重複加入。
        if (_editingTradeRow is { } editingRow &&
            SynthesizeAssetSubject(editingRow) is { } synthesized &&
            !regular.Any(a => IsSameSubject(a, synthesized)))
        {
            regular.Add(synthesized);
        }

        // Step 2：拼成最終 list = 最近使用 group → regular 三大群 → 新增資產 sentinel。
        var final = new List<TxAssetSubject>(regular.Count + Core.Models.AppSettings.MaxRecentlyUsedAssets + 1);

        // P2.6 — 最近使用的資產 group。從 _getRecentAssetIds (LRU 順序) 拿 id，到 regular
        // 裡找對應 row，clone 並改 GroupKey 為「最近」，疊到 final 頂端。原本所屬 group 的
        // 副本仍保留在 regular 區（兩處出現是刻意的：一次當捷徑、一次走完整目錄）。
        // 找不到的 id（已刪除資產）跳過。Guid.Empty 跳過（legacy liability 沒穩定 id，無法 LRU）。
        var recentIds = _getRecentAssetIds?.Invoke();
        if (recentIds is { Count: > 0 })
        {
            foreach (var id in recentIds)
            {
                if (id == Guid.Empty || id == AddNewAssetSentinelId)
                    continue;
                var match = regular.FirstOrDefault(a => a.Id == id);
                if (match is null)
                    continue;
                final.Add(match with { GroupKey = "Portfolio.Tx.Asset.Group.Recent" });
                if (final.Count >= Core.Models.AppSettings.MaxRecentlyUsedAssets)
                    break;
            }
        }

        final.AddRange(regular);

        // 「+ 新增資產」CTA sentinel — 永遠排最後。
        if (_openAddNewAsset is not null)
        {
            final.Add(new TxAssetSubject(
                Kind: TxAssetKind.None,
                Id: AddNewAssetSentinelId,
                PrimaryName: _localize("Portfolio.Tx.Asset.AddNew.Title", "+ 新增資產"),
                SecondaryLine: _localize("Portfolio.Tx.Asset.AddNew.Hint", "建立新的投資項目"),
                GroupKey: "Portfolio.Tx.Asset.Group.AddNew",
                Currency: string.Empty));
        }
        return final;
    }

    /// <summary>
    /// 編輯既有 trade 時找對應的 TxAssetSubject。寬鬆模式：找不到就回 null。
    /// 解析順序：
    /// <list type="number">
    ///   <item>有 PortfolioEntryId → 找 Positions</item>
    ///   <item>Buy/Sell/Dividend 走 Symbol → 找 Positions</item>
    ///   <item>Cash flow (Deposit/Withdrawal/Income/Transfer) → 找 CashAccount by Id</item>
    ///   <item>Loan / CreditCard → 找 Liability</item>
    /// </list>
    /// </summary>
    private TxAssetSubject? ResolveAssetSubjectForTrade(TradeRowViewModel row)
    {
        var assets = AvailableAssets;
        // 1. 直接以 PortfolioEntryId 命中
        if (row.PortfolioEntryId is { } entryId)
        {
            var hit = assets.FirstOrDefault(a =>
                a.Kind is TxAssetKind.Stock or TxAssetKind.Fund or TxAssetKind.Crypto or TxAssetKind.Metal or TxAssetKind.Bond
                && a.Id == entryId);
            if (hit is not null)
                return hit;
        }
        // 2. Buy / Sell / Dividend：走 symbol
        if (row.Type is TradeType.Buy or TradeType.Sell or TradeType.CashDividend or TradeType.StockDividend
            && !string.IsNullOrWhiteSpace(row.Symbol))
        {
            var hit = assets.FirstOrDefault(a =>
                string.Equals(a.Symbol, row.Symbol, StringComparison.OrdinalIgnoreCase));
            if (hit is not null)
                return hit;
        }
        // 3. Cash flow → CashAccount by CashAccountId.
        // Q02 — CashDividend 不在此列：它的標的是投資資產（走 step 2 的 symbol 比對），其 CashAccountId
        // 只是入帳帳戶。若 step 2 因持倉已平倉而 miss，這裡再用 CashAccountId 命中會誤解析成 CashAccount
        // （Kind 錯），導致 cashDiv 不在 AvailableTradeTypes、類型 picker 空白。故讓它落到 step 5 合成投資 subject。
        if (row.Type is TradeType.Deposit or TradeType.Withdrawal or TradeType.Income or TradeType.Transfer
            && row.CashAccountId is { } cashId)
        {
            var hit = assets.FirstOrDefault(a => a.Kind == TxAssetKind.CashAccount && a.Id == cashId);
            if (hit is not null)
                return hit;
        }
        // 4. Loan / CreditCard → Liability
        // P5.16 — 雙路徑 lookup：先嘗試 LiabilityAssetId (GUID) 精確比對，
        // 找不到再 fallback 走 Label 字串配對（兼容 legacy 資料：早期 trade 沒
        // 寫 LiabilityAssetId 或 liability 的 AssetId 為 null，AvailableAssets 線
        // 上的 liability 用 Guid.Empty 哨兵代位，直接 GUID 比對就漏接）。
        if (row.Type is TradeType.CreditCardCharge or TradeType.CreditCardPayment
                or TradeType.LoanBorrow or TradeType.LoanRepay)
        {
            // 4a. 精確：以 GUID 比對
            if (row.LiabilityAssetId is { } liabId)
            {
                var hit = assets.FirstOrDefault(a =>
                    a.Kind == TxAssetKind.Liability && a.Id == liabId);
                if (hit is not null)
                    return hit;
            }

            // 4b. Fallback：以 Label 字串比對（Loan 走 LoanLabel，CreditCard 走 row.Name）
            var label = row.LoanLabel ?? row.Name;
            if (!string.IsNullOrWhiteSpace(label))
            {
                var hit = assets.FirstOrDefault(a =>
                    a.Kind == TxAssetKind.Liability
                    && string.Equals(a.PrimaryName, label, StringComparison.OrdinalIgnoreCase));
                if (hit is not null)
                    return hit;
            }
        }

        // 5. Q02 — live-collection 全部 miss（標的已不在清單，如已平倉持倉）：合成一個 subject。
        // BuildAvailableAssets 在 _editingTradeRow 設定時已把同一個合成 subject 併入 AvailableAssets，
        // 故此處回傳的物件保證 ∈ ItemsSource（ComboBox 才渲染得出來）。先在 assets 找等價的真 subject
        // 以優先採用真資料，找不到才用合成那筆。
        if (SynthesizeAssetSubject(row) is { } synthesized)
            return assets.FirstOrDefault(a => IsSameSubject(a, synthesized)) ?? synthesized;

        return null;
    }

    /// <summary>
    /// Q02 — 依交易類型從 <see cref="TradeRowViewModel"/> 合成一個 <see cref="TxAssetSubject"/>，
    /// 供 live view 找不到對應標的時 fallback 用（典型情境：已平倉持倉被 ShowClosedPositions 預設關濾掉）。
    /// Kind 對齊交易類型的標的種類：Buy/Sell/Dividend → Investment、Loan/CreditCard → Liability、
    /// Cash flow → CashAccount。資料不足以合成（缺 Symbol/帳戶 id/標籤）時回 null。
    /// </summary>
    private TxAssetSubject? SynthesizeAssetSubject(TradeRowViewModel row)
    {
        switch (row.Type)
        {
            // 投資類：用 Symbol 當識別，Stock kind 涵蓋 buy/sell/cashDiv/stockDiv 四個 type（見
            // ResolveAvailableTypeKeys）。幣別取 row.InstrumentCurrency（DB 預設 "TWD"）。
            case TradeType.Buy or TradeType.Sell or TradeType.CashDividend or TradeType.StockDividend:
                if (string.IsNullOrWhiteSpace(row.Symbol))
                    return null;
                var ccy = string.IsNullOrWhiteSpace(row.InstrumentCurrency) ? "TWD" : row.InstrumentCurrency;
                var name = string.IsNullOrWhiteSpace(row.Name) ? row.Symbol : row.Name;
                return new TxAssetSubject(
                    Kind: TxAssetKind.Stock,
                    Id: row.PortfolioEntryId ?? Guid.Empty,
                    PrimaryName: name,
                    SecondaryLine: $"({row.Symbol}) · {ccy}",
                    GroupKey: "Portfolio.Tx.Asset.Group.Investment",
                    Currency: ccy,
                    Symbol: row.Symbol);

            // 負債類：貸款以 LoanLabel 當名稱、信用卡以 row.Name；Id 取 LiabilityAssetId（沒有則哨兵 Empty）。
            case TradeType.LoanBorrow or TradeType.LoanRepay or TradeType.CreditCardCharge or TradeType.CreditCardPayment:
                var label = row.Type is TradeType.LoanBorrow or TradeType.LoanRepay
                    ? row.LoanLabel ?? row.Name
                    : row.Name;
                if (string.IsNullOrWhiteSpace(label))
                    return null;
                return new TxAssetSubject(
                    Kind: TxAssetKind.Liability,
                    Id: row.LiabilityAssetId ?? Guid.Empty,
                    PrimaryName: label,
                    SecondaryLine: "DEBT",
                    GroupKey: "Portfolio.Tx.Asset.Group.Liability",
                    Currency: string.IsNullOrWhiteSpace(row.SettlementCurrency) ? "TWD" : row.SettlementCurrency);

            // 現金流：以 CashAccountId 當識別。帳戶仍存在通常 step 3 已命中，此處覆蓋帳戶被刪等罕見情形。
            case TradeType.Deposit or TradeType.Withdrawal or TradeType.Income or TradeType.Transfer:
                if (row.CashAccountId is not { } accId)
                    return null;
                return new TxAssetSubject(
                    Kind: TxAssetKind.CashAccount,
                    Id: accId,
                    PrimaryName: string.IsNullOrWhiteSpace(row.Name) ? "—" : row.Name,
                    SecondaryLine: "CASH",
                    GroupKey: "Portfolio.Tx.Asset.Group.Cash",
                    Currency: string.IsNullOrWhiteSpace(row.SettlementCurrency) ? "TWD" : row.SettlementCurrency,
                    SuggestedCashAccountId: accId);

            default:
                return null;
        }
    }

    /// <summary>
    /// Q02 — 兩個 subject 是否指同一資產：同 Kind 且（Id 相同且非 Empty）或（投資類 Symbol 相同）。
    /// 用於去重，讓真實 subject 優先於合成 subject。
    /// </summary>
    private static bool IsSameSubject(TxAssetSubject a, TxAssetSubject b)
    {
        if (a.Kind != b.Kind)
            return false;
        if (a.Id != Guid.Empty && a.Id == b.Id)
            return true;
        return IsInvestmentAssetKind(a.Kind)
            && !string.IsNullOrWhiteSpace(a.Symbol)
            && string.Equals(a.Symbol, b.Symbol, StringComparison.OrdinalIgnoreCase);
    }

    private TxAssetSubject? ResolveAssetSubjectForPosition(PortfolioRowViewModel row)
    {
        var assets = AvailableAssets;
        var hit = assets.FirstOrDefault(a =>
            IsInvestmentAssetKind(a.Kind) &&
            a.GroupKey != "Portfolio.Tx.Asset.Group.Recent" &&
            a.Id == row.Id);
        if (hit is not null)
            return hit;

        if (string.IsNullOrWhiteSpace(row.Symbol))
            return null;

        return assets.FirstOrDefault(a =>
            IsInvestmentAssetKind(a.Kind) &&
            a.GroupKey != "Portfolio.Tx.Asset.Group.Recent" &&
            string.Equals(a.Symbol, row.Symbol, StringComparison.OrdinalIgnoreCase) &&
            (string.IsNullOrWhiteSpace(row.Currency) ||
             string.Equals(a.Currency, row.Currency, StringComparison.OrdinalIgnoreCase)));
    }

    private TxAssetSubject? ResolveAssetSubjectForLiability(LiabilityRowViewModel row)
    {
        var assets = AvailableAssets;
        if (row.AssetId is { } assetId)
        {
            var hit = assets.FirstOrDefault(a =>
                a.Kind == TxAssetKind.Liability &&
                a.GroupKey != "Portfolio.Tx.Asset.Group.Recent" &&
                a.Id == assetId);
            if (hit is not null)
                return hit;
        }

        return assets.FirstOrDefault(a =>
            a.Kind == TxAssetKind.Liability &&
            a.GroupKey != "Portfolio.Tx.Asset.Group.Recent" &&
            string.Equals(a.PrimaryName, row.Label, StringComparison.OrdinalIgnoreCase));
    }

    private TxAssetSubject? ResolveAssetSubjectForCashAccount(CashAccountRowViewModel row) =>
        row is null
            ? null
            : AvailableAssets.FirstOrDefault(a =>
                a.Kind == TxAssetKind.CashAccount &&
                a.GroupKey != "Portfolio.Tx.Asset.Group.Recent" &&
                a.Id == row.Id);

    private static TxAssetKind MapAssetType(Core.Models.AssetType t) => t switch
    {
        Core.Models.AssetType.Stock => TxAssetKind.Stock,
        Core.Models.AssetType.Fund => TxAssetKind.Fund,
        Core.Models.AssetType.Crypto => TxAssetKind.Crypto,
        Core.Models.AssetType.PreciousMetal => TxAssetKind.Metal,
        Core.Models.AssetType.Bond => TxAssetKind.Bond,
        _ => TxAssetKind.Stock,
    };

    public bool IsEditMode => EditingTradeId.HasValue;

    /// <summary>
    /// True when the current edit target is a meta-only trade (Sell, or Buy / StockDividend
    /// without a <see cref="Trade.PortfolioEntryId"/> link). Drives the dialog XAML to disable
    /// economic inputs so users know what they can and can't change. Recomputed whenever
    /// the editing trade id changes.
    /// </summary>
    public bool IsEditingMetaOnly =>
        EditingTradeId.HasValue &&
        Trades.FirstOrDefault(t => t.Id == EditingTradeId.Value) is { IsMetaOnlyEditType: true };

    /// <summary>
    /// True when the user can directly edit economic fields (price / quantity / amount /
    /// account / etc.) on the dialog. Two cases enable it:
    /// <list type="bullet">
    /// <item>Not editing — i.e. creating a new trade.</item>
    /// <item>Editing a non-meta-only type (Income / CashDividend / Buy / StockDividend / …)
    /// where ConfirmTx promotes the save to implicit revision (delete-old + create-new).</item>
    /// </list>
    /// Locks to false only for meta-only types (Sell / Transfer leg), where dependent state
    /// would silently corrupt — user must click 修訂 to consciously do the revision flow.
    /// </summary>
    public bool AreEconomicFieldsEditable => !IsEditMode || !IsEditingMetaOnly;

    /// <summary>
    /// True when the dialog should show the locked-core summary card. Only meta-only edits
    /// need this — non-meta-only edits show the normal form fields pre-filled with live values.
    /// </summary>
    public bool ShowEditLockedSummary => IsEditMode && IsEditingMetaOnly;

    partial void OnEditingTradeIdChanged(Guid? _)
    {
        OnPropertyChanged(nameof(IsEditMode));
        OnPropertyChanged(nameof(IsEditingMetaOnly));
        OnPropertyChanged(nameof(AreEconomicFieldsEditable));
        OnPropertyChanged(nameof(ShowEditLockedSummary));
        OnPropertyChanged(nameof(ShowAssetSelector));
        OnPropertyChanged(nameof(ShowTxCurrencyRow));
        // Edit-mode toggle changes the preview baseline (original delta is now
        // either applicable or zeroed).
        NotifyImpactPreviewChanged();
        CreateRevisionCommand.NotifyCanExecuteChanged();
        DeleteTradeCommand.NotifyCanExecuteChanged();
        RequestDeleteTradeCommand.NotifyCanExecuteChanged();

        if (!EditingTradeId.HasValue)
        {
            IsDeleteConfirmOpen = false;
            DeleteTargetName = string.Empty;
        }
    }

    [ObservableProperty] private string _editSummaryType = string.Empty;
    [ObservableProperty] private string _editSummaryTarget = string.Empty;
    [ObservableProperty] private string _editSummaryAmount = string.Empty;
    [ObservableProperty] private string _editSummaryQuantity = string.Empty;

    // The Tx dialog date picker is bound to TxDate, but ConfirmBuyAsync →
    // AddPosition reads AddBuyDate when building the PortfolioEntry.
    partial void OnTxDateChanged(DateTime value)
    {
        if (value != value.Date)
        {
            TxDate = value.Date;
            return;
        }

        AddAssetDialog.AddBuyDate = value;
        QueueBuyFxRateRefresh();
        // P5.8b prereq — mirror Buy: date change invalidates auto-resolved FX
        // for Sell + Dividend too.
        QueueSellFxRateRefresh();
        QueueDividendFxRateRefresh();
    }
    [ObservableProperty] private DateTime _txDate = DateTime.Today;
    [ObservableProperty] private string _txError = string.Empty;

    // ── 欄位驗證訊息（空字串 = 無錯誤；綁定到 XAML FormFieldError TextBlock）──
    [ObservableProperty] private string _txAmountError = string.Empty;
    [ObservableProperty] private string _txFeeError = string.Empty;
    // Div errors moved to Div (DividendTxViewModel).
    // Transfer / Loan errors moved to Transfer / Loan sub-VMs (H1-P3 phase 3).
    [ObservableProperty] private string _txCommissionDiscountError = string.Empty;

    /// <summary>
    /// H1 — buy transaction cluster extracted into <see cref="Tx.BuyTxViewModel"/>.
    /// All buy-related state (AssetType / MetaOnly / PriceMode / TotalCost /
    /// TotalIncludesFee / TotalCostError + computed predicates) now lives here.
    /// Bind XAML directly to <c>Buy.X</c>.
    /// </summary>
    public Tx.BuyTxViewModel Buy { get; } = new();

    public string TxBuyAssetType
    {
        get => Buy.AssetType;
        set => Buy.AssetType = value;
    }

    public string TxBuyTotalCostError
    {
        get => Buy.TotalCostError;
        set => Buy.TotalCostError = value;
    }

    private static string ValidatePositiveDecimalOrEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty :
        !ParseHelpers.TryParseDecimal(value, out var v) || v <= 0 ? "請輸入大於 0 的數字" : string.Empty;

    private static string ValidateNonNegativeDecimalOrEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty :
        !ParseHelpers.TryParseDecimal(value, out var v) || v < 0 ? "請輸入 0 或以上的數字" : string.Empty;

    private static string ValidatePositiveIntOrEmpty(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;
        if (!long.TryParse(value.Replace(",", ""), out var l) || l <= 0)
            return "請輸入大於 0 的整數";
        if (l > int.MaxValue)
            return $"數量超過上限（最大 {int.MaxValue:N0}）";
        return string.Empty;
    }

    private static string ValidateCommissionDiscountOrEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty :
        !ParseHelpers.TryParseDecimal(value, out var v) || v <= 0 || v > 1 ? "折扣需介於 0.01 到 1.0" : string.Empty;

    // 收入欄位
    [ObservableProperty] private string _txAmount = string.Empty;
    [ObservableProperty] private string _txNote = string.Empty;

    // IncomeNotes preset list removed — IncomeTxForm now uses a free-text TextBox
    // for TxNote. Income source semantics live entirely in TxCategoryId, with the
    // shared IncomeCategories collection driving the dropdown.

    // 現金帳戶選擇（收入 + 現金股利共用，null = 不連動）
    [ObservableProperty] private CashAccountRowViewModel? _txCashAccount;

    // 股利欄位（現金 + 股票）— 全部移到 Div (DividendTxViewModel)。
    public Tx.DividendTxViewModel Div { get; } = new();

    // 手續費折扣 — UI 從 dialog 移到設定頁。dialog 仍持此屬性以便下游 fee 計算
    // 公式（折扣 × 標準費率）不變；初始值由 SeedCommissionDiscountFromSettings()
    // 在 OpenTxDialog / EditTrade 時從 AppSettings.DefaultCommissionDiscount 帶入。
    [ObservableProperty] private string _txCommissionDiscount = "1.0";

    /// <summary>
    /// 從 <see cref="AppSettings.DefaultCommissionDiscount"/> 帶 預設值到本 VM。
    /// 在 dialog 開啟 / 進入編輯模式時呼叫；編輯既有交易時 TxCommissionDiscount 會被
    /// EditTrade 後續邏輯覆寫成該筆原本的值，因此 seed 不會干擾編輯流程。
    /// </summary>
    private void SeedCommissionDiscountFromSettings()
    {
        var defaultDisc = _getDefaultCommissionDiscount?.Invoke() ?? 1.0m;
        // 0.1 ~ 1.0 區間外的歷史壞值直接回 1.0；TextBox 用 "1.0" / "0.6" 等顯示格式。
        if (defaultDisc <= 0m || defaultDisc > 1m)
            defaultDisc = 1.0m;
        TxCommissionDiscount = defaultDisc.ToString("0.##");
    }

    /// <summary>解析後的折扣值，用於計算。</summary>
    public decimal TxCommissionDiscountValue =>
        ParseHelpers.TryParseDecimal(TxCommissionDiscount, out var v) && v is > 0 and <= 1 ? v : 1m;

    partial void OnTxCommissionDiscountChanged(string value)
    {
        TxCommissionDiscountError = ValidateCommissionDiscountOrEmpty(value);
        AddAssetDialog.UpdateBuyPreview();
        UpdateSellTxPreview();
    }

    // 買入欄位 — 全部移到 Buy (BuyTxViewModel)。

    // 賣出欄位
    /// <summary>
    /// H1 — sell transaction state cluster extracted into <see cref="Tx.SellTxViewModel"/>.
    /// All sell-related state (Position / Quantity / preview values / IsEtf flags)
    /// now lives here. XAML binds directly to <c>Sell.X</c>.
    /// </summary>
    public Tx.SellTxViewModel Sell { get; } = new();

    private void OnSellTxChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(SellTxViewModel.Position):
                if (!Sell.SuppressPositionPriceAutoFill &&
                    Sell.Position is { CurrentPrice: > 0 } pos)
                    TxAmount = pos.CurrentPrice.ToString("F2");
                if (Sell.Position is null)
                    Sell.Quantity = string.Empty;
                Sell.IsEtf = Sell.Position is not null && _search.IsEtf(Sell.Position.Symbol);
                Sell.IsBondEtf = Sell.Position is not null && _search.IsBondEtf(Sell.Position.Symbol);
                // P3 — sync instrument currency from chosen position so SellTxForm can
                // show cross-currency banner + FX rate field via Sell.IsCrossCurrency.
                Sell.InstrumentCurrency = Sell.Position is null
                    ? string.Empty
                    : Assetra.Core.Models.StockExchangeRegistry.ResolveDefaultCurrency(Sell.Position.Exchange);
                UpdateSellTxPreview();
                break;
            case nameof(SellTxViewModel.Quantity):
                Sell.QuantityError = ValidatePositiveIntOrEmpty(Sell.Quantity);
                UpdateSellTxPreview();
                break;
            case nameof(SellTxViewModel.ActualCashAmount):
                Sell.ActualCashAmountError = ValidatePositiveDecimalOrEmpty(Sell.ActualCashAmount);
                NotifyImpactPreviewChanged();
                break;
            case nameof(SellTxViewModel.FxRate):
                // P5.8b prereq — mirror Buy: empty allowed (= implicit 1.0). Track
                // manual override so subsequent QueueSellFxRateRefresh() skips the
                // user's value unless they hit Fetch explicitly.
                Sell.FxRateError = ValidatePositiveDecimalOrEmpty(Sell.FxRate);
                if (!_suppressSellFxManualTracking)
                {
                    Sell.IsFxManual = !string.IsNullOrWhiteSpace(Sell.FxRate);
                    if (Sell.IsFxManual)
                    {
                        Sell.FxSourceLabel = "manual";
                        Sell.FxRateDate ??= DateOnly.FromDateTime(TxDate.Date);
                    }
                }
                NotifyImpactPreviewChanged();
                break;
            case nameof(SellTxViewModel.InstrumentCurrency):
            case nameof(SellTxViewModel.CashAccountCurrency):
            case nameof(SellTxViewModel.SettlementCurrency):
                FetchSellFxRateCommand.NotifyCanExecuteChanged();
                QueueSellFxRateRefresh();
                NotifyImpactPreviewChanged();
                break;
            case nameof(SellTxViewModel.SettlementInputMode):
                NotifyImpactPreviewChanged();
                break;
        }
    }

    /// <summary>
    /// Recomputes the sell-tx preview (手續費 + 證交稅 + 實得淨額) whenever the sell
    /// price, discount, selected position, or manual fee override changes.
    /// Mirrors <c>UpdateBuyPreview</c> but for the sell side.
    /// </summary>
    private void UpdateSellTxPreview()
    {
        if (!TxTypeIsSell ||
            Sell.Position is null ||
            !ParseHelpers.TryParseDecimal(TxAmount, out var price) || price <= 0)
        {
            Sell.ResetPreview();
            return;
        }

        var qty = ParseHelpers.TryParseInt(Sell.Quantity, out var parsedQty) && parsedQty > 0
            ? parsedQty
            : (int)Sell.Position.Quantity;
        var gross = price * qty;

        // Manual fee override: user has typed a value in the 手續費 field → treat that as
        // commission + tax combined (same convention as ConfirmSell). Leave the individual
        // breakdown at 0 so the preview card doesn't lie about the split.
        if (!string.IsNullOrWhiteSpace(TxFee) &&
            ParseHelpers.TryParseDecimal(TxFee, out var manualFee) && manualFee >= 0)
        {
            Sell.GrossAmount = gross;
            Sell.Commission = manualFee;
            Sell.TransactionTax = 0;
            Sell.NetAmount = gross - manualFee;
            return;
        }

        var discount = TxCommissionDiscountValue;
        var fee = TaiwanTradeFeeCalculator.CalcSell(price, qty, discount, Sell.IsEtf, Sell.IsBondEtf);
        Sell.GrossAmount = fee.GrossAmount;
        Sell.Commission = fee.Commission;
        Sell.TransactionTax = fee.TransactionTax;
        Sell.NetAmount = fee.NetAmount;
    }

    // Type booleans
    public bool TxTypeIsIncome => TxType == "income";
    public bool TxTypeIsCashDiv => TxType == "cashDiv";
    public bool TxTypeIsStockDiv => TxType == "stockDiv";
    public bool TxTypeIsCashFlow => TxType is "deposit" or "withdrawal";
    /// <summary>True 僅當「提款」（支出）。分類欄只在提款顯示；「存入」是把外部資金搬入、非收支，不分類。</summary>
    public bool TxTypeIsWithdrawal => TxType == "withdrawal";
    public bool TxTypeIsLoan => TxType is "loanBorrow" or "loanRepay";
    public bool TxTypeIsLoanBorrow => TxType == "loanBorrow";
    public bool TxTypeIsLoanRepay => TxType == "loanRepay";
    public bool TxTypeIsCreditCard => TxType is "creditCardCharge" or "creditCardPayment";
    public bool TxTypeIsCreditCardCharge => TxType == "creditCardCharge";
    public bool TxTypeIsCreditCardPayment => TxType == "creditCardPayment";
    /// <summary>True for 轉帳 — money moves between two cash accounts.</summary>
    public bool TxTypeIsTransfer => TxType == "transfer";
    public bool TxTypeIsBuy => TxType == "buy";
    public bool TxTypeIsSell => TxType == "sell";

    /// <summary>
    /// Portfolio-Groups-Refactor P3 — TxType 是「投資相關」（買/賣/股利）才需暴露
    /// 群組 ComboBox。現金流 / 借款 / 信用卡 / 轉帳 / 收入 概念上屬於 default group。
    /// </summary>
    public bool TxTypeSupportsGroupSelection => TxType is "buy" or "sell" or "cashDiv" or "stockDiv";

    /// <summary>
    /// Composite visibility flag for the group ComboBox at the top of the per-type form
    /// area: catalog must be loaded AND the current TxType must benefit from grouping.
    /// </summary>
    public bool ShouldShowGroupSelector => IsGroupSelectorVisible && TxTypeSupportsGroupSelection;

    /// <summary>
    /// Dialog 動態標題的 i18n resource key。依使用者選的 TxType 切換成「新增買入交易」、
    /// 「新增信用卡消費」等。XAML 用 ResourceKeyToStringConverter 解析此 key。
    /// 編輯模式維持 Portfolio.Tx.EditTitle（在 XAML DataTrigger 處理）。
    /// </summary>
    /// <summary>
    /// P2.15 — 統一回傳「新增交易」(Portfolio.Record.Title)。原本依 TxType 切
    /// 「新增買入交易 / 新增賣出交易 ...」是 Phase 2 之前的設計，當時 user 點 menu
    /// 已選好類型才開 dialog。Phase 2 之後 type picker 移到 dialog 內，user 開啟
    /// 那瞬間 TxType 還沒選擇 / 是預設值，title 動態切就沒意義反而干擾 (e.g. 顯示
    /// 「新增買入交易」但其實還沒挑類型)。Edit 模式仍由 XAML DataTrigger 切到
    /// Portfolio.Tx.EditTitle，不受這裡影響。
    /// </summary>
    public string TxDynamicTitleKey => "Portfolio.Record.Title";

    /// <summary>「此筆交易會儲存於 ___ 」的 i18n resource key。null = 沒選類型，提示隱藏。</summary>
    public string? TxDestinationKey => TxType switch
    {
        "buy" => "Portfolio.Record.Destination.Buy",
        "sell" => "Portfolio.Record.Destination.Sell",
        "cashDiv" => "Portfolio.Record.Destination.CashDiv",
        "stockDiv" => "Portfolio.Record.Destination.StockDiv",
        "income" => "Portfolio.Record.Destination.Income",
        "deposit" => "Portfolio.Record.Destination.Deposit",
        "withdrawal" => "Portfolio.Record.Destination.Withdrawal",
        "transfer" => "Portfolio.Record.Destination.Transfer",
        "loanBorrow" => "Portfolio.Record.Destination.LoanBorrow",
        "loanRepay" => "Portfolio.Record.Destination.LoanRepay",
        "creditCardCharge" => "Portfolio.Record.Destination.CreditCardCharge",
        "creditCardPayment" => "Portfolio.Record.Destination.CreditCardPayment",
        _ => null,
    };

    public bool HasTxDestination => TxDestinationKey is not null;

    public bool TxBuyIsStock => TxTypeIsBuy && Buy.IsStock;
    public bool TxBuyIsNonStock => TxTypeIsBuy && Buy.IsNonStock;
    public bool TxBuyIsCrypto => TxTypeIsBuy && Buy.IsCrypto;
    public bool TxBuyIsUnitMode => Buy.IsUnitMode;
    public bool TxBuyIsTotalMode => Buy.IsTotalMode;

    public string TxCashAccountLabel => TxType switch
    {
        "deposit" => L("Portfolio.Tx.DepositAccount", "存入帳戶"),
        "withdrawal" => L("Portfolio.Tx.WithdrawalAccount", "扣款帳戶"),
        "income" => L("Portfolio.Tx.IncomeAccount", "入帳帳戶"),
        "cashDiv" => L("Portfolio.Tx.DividendDepositAccount", "股利入帳帳戶"),
        "buy" => L("Portfolio.Tx.PaymentAccount", "扣款帳戶"),
        "sell" => L("Portfolio.Tx.ProceedsAccount", "入帳帳戶"),
        "loanBorrow" => L("Portfolio.Tx.LoanDisbursementAccount", "撥款帳戶"),
        "loanRepay" => L("Portfolio.Tx.PaymentAccount", "扣款帳戶"),
        "creditCardPayment" => L("Portfolio.Tx.CreditCardPaymentAccount", "扣款帳戶"),
        _ => L("Portfolio.Tx.CashAccount", "帳戶")
    };

    public string TxUseCashAccountLabel => TxType switch
    {
        "buy" or "withdrawal" or "loanRepay" => L("Portfolio.Tx.UseDeductCashAccount", "從帳戶扣款"),
        "sell" or "deposit" or "income" or "cashDiv" or "loanBorrow" => L("Portfolio.Tx.UseDepositCashAccount", "存入帳戶"),
        _ => L("Portfolio.Tx.UseLoanCashAccount", "連動帳戶")
    };

    public string TxCashFlowHint => TxType switch
    {
        "deposit" => L("Portfolio.Tx.DepositHint", "把外部資金存入自己的帳戶，會增加該帳戶餘額。"),
        "withdrawal" => L("Portfolio.Tx.WithdrawalHint", "提款、轉帳給別人或支付現金支出；只扣款帳戶，不建立轉入帳戶。"),
        _ => string.Empty
    };

    public string TxTransferHint =>
        L("Portfolio.Tx.TransferHint", "自己的帳戶間移轉；來源扣款、目標入帳，總資產不變。轉給別人請用「提款」。");

    public string TxCreditCardHint => TxType switch
    {
        "creditCardCharge" => L("Portfolio.Tx.CreditCardChargeHint", "信用卡消費會增加信用卡負債，支出與淨資產影響會記在消費日。"),
        "creditCardPayment" => L("Portfolio.Tx.CreditCardPaymentHint", "信用卡繳款會扣現金帳戶並降低信用卡負債；若消費已先記錄，繳款當下淨資產通常不變。"),
        _ => string.Empty
    };

    // Buy sub-type predicates moved next to the AssetType facade in the Buy block.

    /// <summary>
    /// H1 — loan transaction state cluster extracted into <see cref="Tx.LoanTxViewModel"/>.
    /// Covers Label, Principal/InterestPaid (LoanRepay), Rate/TermMonths/StartDate
    /// (LoanBorrow amortization).
    /// </summary>
    public Tx.LoanTxViewModel Loan { get; } = new();

    /// <summary>
    /// Suggestions for the editable loan-label ComboBox — derived from already-recorded loans.
    /// </summary>
    public IReadOnlyList<string> LoanLabelSuggestions =>
        Liabilities.Select(l => l.Label).OrderBy(l => l).ToList();

    /// <summary>
    /// H1-P9 — credit-card transaction state extracted into <see cref="Tx.CreditCardTxViewModel"/>.
    /// </summary>
    public Tx.CreditCardTxViewModel CreditCard { get; } = new();

    public IReadOnlyList<LiabilityRowViewModel> CreditCardOptions =>
        Liabilities.Where(l => !l.IsLoan).OrderBy(l => l.Label).ToList();

    /// <summary>
    /// H1 — transfer transaction state cluster extracted into <see cref="Tx.TransferTxViewModel"/>.
    /// Covers Target / TargetName / TargetAmount / TargetAmountError / ImpliedRate.
    /// Source amount stays on dialog VM (TxAmount, shared across types).
    /// </summary>
    public Tx.TransferTxViewModel Transfer { get; } = new();

    partial void OnTxAmountChanged(string value)
    {
        TxAmountError = ValidatePositiveDecimalOrEmpty(value);
        // Push source-amount text into Transfer VM so its ImpliedRate refreshes.
        Transfer.SourceAmountText = value;
        UpdateSellTxPreview();
        NotifyImpactPreviewChanged();
    }

    partial void OnTxFeeChanged(string value)
    {
        TxFeeError = ValidateNonNegativeDecimalOrEmpty(value);
        AddAssetDialog.UpdateBuyPreview();
        UpdateSellTxPreview();
    }

    /// <summary>
    /// Optional fee that goes with the transaction. Same semantics for every type that
    /// supports it (loan / cash flow / transfer):
    /// <list type="bullet">
    /// <item><description><b>借款</b>：撥款時扣繳的手續費／開辦費 → 現金實際入帳 <c>amount − fee</c></description></item>
    /// <item><description><b>還款</b>：銀行還款處理費 → 現金實際扣款 <c>amount + fee</c></description></item>
    /// <item><description><b>存入 / 提款</b>：跨行匯款手續費、ATM 費用 → 同上模式</description></item>
    /// <item><description><b>轉帳</b>：跨行轉帳費 → 從來源現金帳戶扣除</description></item>
    /// </list>
    /// 實作：fee &gt; 0 時會額外建一筆 <see cref="TradeType.Withdrawal"/> 連動到同一個現金帳戶，
    /// 確保現金流與銀行對帳單一致。
    /// </summary>
    [ObservableProperty] private string _txFee = string.Empty;

    // ── Lazy Upsert scaffolding — bound by Task 19 XAML (editable ComboBox) ─────────
    // When the user types a cash-account name that doesn't match any existing row,
    // the Confirm commands call IAccountUpsertWorkflowService.FindOrCreateAccountAsync on submit.
    // The ComboBox's SelectedItem (TxCashAccount) remains authoritative when set;
    // TxCashAccountName only wins when TxCashAccount is null AND the name is non-empty.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNewCashAccount))]
    private string _txCashAccountName = string.Empty;

    public bool IsNewCashAccount =>
        !string.IsNullOrWhiteSpace(TxCashAccountName)
        && !CashAccountSuggestions.Any(s =>
            string.Equals(s, TxCashAccountName, StringComparison.OrdinalIgnoreCase));

    private void SyncTxCashAccountNameFromSelection(CashAccountRowViewModel? account)
    {
        if (account is null)
            return;

        if (!string.Equals(TxCashAccountName, account.Name, StringComparison.Ordinal))
            TxCashAccountName = account.Name;
    }

    private readonly ObservableCollection<string> _cashAccountSuggestions = new();
    public ReadOnlyObservableCollection<string> CashAccountSuggestions { get; }

    /// <summary>Used by PortfolioViewModel.ApplyCashAccounts to refresh the suggestion list.</summary>
    internal void ReplaceCashAccountSuggestions(IEnumerable<string> names)
    {
        _cashAccountSuggestions.Clear();
        foreach (var n in names)
            _cashAccountSuggestions.Add(n);
    }

    // ── Transfer destination — same coexistence pattern as TxCashAccountName ──────────
    // INVARIANT: Transfer.Target (SelectedItem picker) wins when set.
    // Transfer.TargetName (typed text) only wins when Transfer.Target is null
    // AND the name is non-empty. IsNewTransferTarget drives the "will create new" hint.
    // Task 19.
    /// <summary>
    /// Transfer destination — typed text coexisting with Transfer.Target (the picker).
    /// When the text doesn't match any existing account, IsNewTransferTarget returns true
    /// and ConfirmTransferAsync routes through FindOrCreateAccountAsync on the destination side.
    /// Task 19.
    /// </summary>
    [NotifyPropertyChangedFor(nameof(IsNewTransferTarget))]
    [ObservableProperty] private string _txTransferTargetName = string.Empty;

    // INVARIANT: picker-first-then-text — same contract as IsNewCashAccount.
    public bool IsNewTransferTarget =>
        !string.IsNullOrWhiteSpace(Transfer.TargetName)
        && !CashAccountSuggestions.Any(s =>
            string.Equals(s, Transfer.TargetName, StringComparison.OrdinalIgnoreCase));

    // Same pattern for positions — task 19 will bind a free-text symbol input here
    // (currently `AddSymbol` is the only free-text symbol field and it lives on the
    // Add dialog, not the TX form). This scaffolding pre-stages the TX-form path.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNewPosition))]
    private string _txSymbolInput = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNewPosition))]
    private string _txExchangeInput = string.Empty;

    public bool IsNewPosition =>
        !string.IsNullOrWhiteSpace(TxSymbolInput)
        && !PositionSuggestions.Any(p =>
            string.Equals(p.Symbol, TxSymbolInput, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(p.Exchange, TxExchangeInput, StringComparison.OrdinalIgnoreCase));

    private readonly ObservableCollection<PositionSuggestion> _positionSuggestions = new();
    public ReadOnlyObservableCollection<PositionSuggestion> PositionSuggestions { get; }

    /// <summary>Used by PortfolioViewModel to push a fresh suggestion snapshot.</summary>
    internal void ReplacePositionSuggestions(IEnumerable<PositionSuggestion> suggestions)
    {
        _positionSuggestions.Clear();
        foreach (var s in suggestions)
            _positionSuggestions.Add(s);
    }

    /// <summary>
    /// Suggestion row for position autocomplete. Record with init-only fields so
    /// the collection is effectively immutable per entry.
    /// </summary>
    public sealed record PositionSuggestion(Guid Id, string Symbol, string Exchange, string DisplayName)
    {
        public string Display => string.IsNullOrEmpty(DisplayName) ? Symbol : $"{Symbol} {DisplayName}";
    }

    /// <summary>
    /// Resolve the cash account for a TX submission.
    /// Priority:
    ///   1. TxCashAccount (ComboBox SelectedItem) — authoritative when set.
    ///   2. TxCashAccountName (typed text) → FindOrCreateAccountAsync. TWD default currency.
    ///   3. null — no cash linkage.
    /// </summary>
    /// <summary>
    /// P3 — resolves the **instrument** currency for the current Buy form state.
    /// Order of preference:
    ///   1. <c>AddSymbolCurrency</c> (filled by autocomplete suggestion — most trusted)
    ///   2. <c>AddExchange</c> → <c>StockExchangeRegistry.ResolveDefaultCurrency</c>
    ///   3. fallback "TWD"
    /// Returns the upper-cased ISO 4217 code. Mirrors <c>AddAssetDialogViewModel.ResolveInstrumentCurrencyForBuy</c>
    /// but uses only data already in-memory (no symbol re-search).
    /// </summary>
    private string ResolveCurrentInstrumentCurrency()
    {
        if (!string.IsNullOrWhiteSpace(AddAssetDialog.AddSymbolCurrency))
            return AddAssetDialog.AddSymbolCurrency.Trim().ToUpperInvariant();
        if (!string.IsNullOrWhiteSpace(Buy.InstrumentCurrency))
            return Buy.InstrumentCurrency.Trim().ToUpperInvariant();
        if (!string.IsNullOrWhiteSpace(AddAssetDialog.AddExchange))
            return Assetra.Core.Models.StockExchangeRegistry
                .ResolveDefaultCurrency(AddAssetDialog.AddExchange.Trim());
        return "TWD";
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0051:Remove unused private members", Justification = "Used by Task 19 XAML binding")]
    private async Task<Guid?> ResolveCashAccountIdAsync(bool requireAccount = false)
    {
        if (TxCashAccount is not null)
            return TxCashAccount.Id;

        if (!string.IsNullOrWhiteSpace(TxCashAccountName) && _accountUpsert is not null)
        {
            // Currency defaults to TWD — matches AddAccountAsync default. Task 19 may
            // thread an explicit currency when the TX form adds a currency picker.
            var id = await _accountUpsert
                .FindOrCreateAccountAsync(TxCashAccountName.Trim(), "TWD")
                .ConfigureAwait(true);
            return id;
        }

        return requireAccount ? (Guid?)null : null; // caller decides whether null is fatal
    }

    /// <summary>
    /// Destination-side analogue of <see cref="ResolveCashAccountIdAsync"/> for
    /// Transfer's target account. Same two-tier resolution:
    ///   1. Transfer.Target (ComboBox SelectedItem) — authoritative when set.
    ///   2. Transfer.TargetName (typed text) → FindOrCreateAccountAsync. TWD default currency.
    /// Task 19.
    /// </summary>
    // INVARIANT: picker-first-then-text — Transfer.Target.Id wins when the picker has a
    // selection; only falls through to FindOrCreateAccountAsync when Transfer.Target is null.
    private async Task<Guid?> ResolveTransferTargetIdAsync()
    {
        if (Transfer.Target is not null)
            return Transfer.Target.Id;

        if (!string.IsNullOrWhiteSpace(Transfer.TargetName) && _accountUpsert is not null)
        {
            return await _accountUpsert
                .FindOrCreateAccountAsync(Transfer.TargetName.Trim(), "TWD")
                .ConfigureAwait(true);
        }
        return null;
    }

    /// <summary>
    /// True（預設）= 借款/還款連動到選定的 <see cref="TxCashAccount"/>；
    /// False = 只調 Balance，不動現金。用勾選框在 dialog 裡明確切換（比起
    /// 「空白下拉 = 不連動」更清楚）。
    /// </summary>
    [ObservableProperty] private bool _txUseCashAccount = true;

    /// <summary>
    /// Checkbox 切換時同步 TxCashAccount：關掉時清空；打開時若尚未選、就帶入
    /// 第一個現金帳戶作為預設。
    /// </summary>
    partial void OnTxUseCashAccountChanged(bool value)
    {
        if (!value)
        {
            TxCashAccount = null;
            TxCashAccountName = string.Empty;
        }
        else if (TxCashAccount is null)
        {
            // 優先使用使用者設定的預設帳戶，其次才是列表第一筆
            TxCashAccount = _getDefaultCashAccount()
                            ?? (CashAccounts.Count > 0 ? CashAccounts[0] : null);
        }

        SyncTxCashAccountNameFromSelection(TxCashAccount);
    }

    partial void OnTxTypeChanged(string value)
    {
        OnPropertyChanged(nameof(TxTypeIsIncome));
        OnPropertyChanged(nameof(TxTypeIsCashDiv));
        OnPropertyChanged(nameof(TxTypeIsStockDiv));
        OnPropertyChanged(nameof(TxTypeIsCashFlow));
        OnPropertyChanged(nameof(TxTypeIsWithdrawal));
        OnPropertyChanged(nameof(TxTypeIsLoan));
        OnPropertyChanged(nameof(TxTypeIsLoanBorrow));
        OnPropertyChanged(nameof(TxTypeIsLoanRepay));
        OnPropertyChanged(nameof(TxTypeIsCreditCard));
        OnPropertyChanged(nameof(TxTypeIsCreditCardCharge));
        OnPropertyChanged(nameof(TxTypeIsCreditCardPayment));
        OnPropertyChanged(nameof(TxTypeIsTransfer));
        OnPropertyChanged(nameof(TxTypeIsBuy));
        OnPropertyChanged(nameof(TxTypeIsSell));
        OnPropertyChanged(nameof(TxTypeSupportsGroupSelection));
        OnPropertyChanged(nameof(ShouldShowGroupSelector));
        // Buy.IsStock/IsNonStock/IsCrypto no longer gate on TxType — XAML form
        // visibility is gated by TxTypeIsBuy on the parent StackPanel, which
        // is already raised above. No notification needed for Buy.X here.
        OnPropertyChanged(nameof(TxCashAccountLabel));
        OnPropertyChanged(nameof(TxUseCashAccountLabel));
        OnPropertyChanged(nameof(TxCashFlowHint));
        OnPropertyChanged(nameof(TxTransferHint));
        OnPropertyChanged(nameof(TxCreditCardHint));
        OnPropertyChanged(nameof(CashFlowCategories));
        // Dialog 動態標題與「顯示位置」提示 — 依當前 TxType 切換 i18n key。
        OnPropertyChanged(nameof(TxDynamicTitleKey));
        OnPropertyChanged(nameof(TxDestinationKey));
        OnPropertyChanged(nameof(HasTxDestination));
        TxError = string.Empty;
        ApplyAutoCategoryFromNote();
        UpdateSellTxPreview();
        NotifyImpactPreviewChanged();
        if (TxTypeIsLoanRepay)
            _ = AutoFillLoanRepayAsync(Loan.Label);
        FetchBuyFxRateCommand.NotifyCanExecuteChanged();
        QueueBuyFxRateRefresh();
        // P5.8b prereq — type switch invalidates fetch ability for both Sell + Div too.
        FetchSellFxRateCommand.NotifyCanExecuteChanged();
        FetchDividendFxRateCommand.NotifyCanExecuteChanged();
        QueueSellFxRateRefresh();
        QueueDividendFxRateRefresh();
    }

    /// <summary>
    /// React to Div.X changes — runs validation + total recomputation that the old
    /// partial OnTxDiv*Changed handlers performed.
    /// </summary>
    private void OnDividendTxChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(DividendTxViewModel.Position):
                UpdateDivTotal();
                // MultiCurrency-Trade-Refactor P3 — sync instrument currency from
                // chosen dividend position so CashDividendTxForm can show the
                // cross-currency banner + FxRate field via Div.IsCrossCurrency.
                Div.InstrumentCurrency = Div.Position is null
                    ? string.Empty
                    : Assetra.Core.Models.StockExchangeRegistry.ResolveDefaultCurrency(Div.Position.Exchange);
                break;
            case nameof(DividendTxViewModel.PerShare):
                Div.PerShareError = ValidatePositiveDecimalOrEmpty(Div.PerShare);
                UpdateDivTotal();
                break;
            case nameof(DividendTxViewModel.Total):
                NotifyImpactPreviewChanged();
                break;
            case nameof(DividendTxViewModel.TotalInput):
                Div.TotalInputError = ValidatePositiveDecimalOrEmpty(Div.TotalInput);
                break;
            case nameof(DividendTxViewModel.StockNewShares):
                Div.StockNewSharesError = ValidatePositiveIntOrEmpty(Div.StockNewShares);
                break;
            case nameof(DividendTxViewModel.ActualCashAmount):
                Div.ActualCashAmountError = ValidatePositiveDecimalOrEmpty(Div.ActualCashAmount);
                NotifyImpactPreviewChanged();
                break;
            case nameof(DividendTxViewModel.FxRate):
                // P5.8b prereq — mirror Buy / Sell: track manual override so
                // QueueDividendFxRateRefresh skips the user's value unless they
                // hit Fetch explicitly.
                Div.FxRateError = ValidatePositiveDecimalOrEmpty(Div.FxRate);
                if (!_suppressDividendFxManualTracking)
                {
                    Div.IsFxManual = !string.IsNullOrWhiteSpace(Div.FxRate);
                    if (Div.IsFxManual)
                    {
                        Div.FxSourceLabel = "manual";
                        Div.FxRateDate ??= DateOnly.FromDateTime(TxDate.Date);
                    }
                }
                NotifyImpactPreviewChanged();
                break;
            case nameof(DividendTxViewModel.InstrumentCurrency):
            case nameof(DividendTxViewModel.CashAccountCurrency):
            case nameof(DividendTxViewModel.SettlementCurrency):
                FetchDividendFxRateCommand.NotifyCanExecuteChanged();
                QueueDividendFxRateRefresh();
                NotifyImpactPreviewChanged();
                break;
            case nameof(DividendTxViewModel.SettlementInputMode):
                NotifyImpactPreviewChanged();
                break;
        }
    }

    private void UpdateDivTotal()
    {
        if (Div.Position is null || !ParseHelpers.TryParseDecimal(Div.PerShare, out var perShare) || perShare <= 0)
            Div.Total = 0;
        else
            Div.Total = perShare * Div.Position.Quantity;
    }

    /// <summary>
    /// 總計顯示文字。注入 <see cref="AddAssetDialogViewModel"/> 的 AddPrice / AddQuantity
    /// 後委派給 <see cref="Tx.BuyTxViewModel.ComputeTotalDisplay"/>。XAML 直接 bind 此屬性
    /// （Buy.ComputedTotalDisplay 不知道 AddPrice/Qty 故無法 self-compute）。
    /// </summary>
    public string TxBuyComputedTotalDisplay =>
        Buy.ComputeTotalDisplay(AddAssetDialog.AddPrice, AddAssetDialog.AddQuantity);

    /// <summary>
    /// P2.4 — 總額模式下方的「單位價格: X」inline mirror。Buy.TotalCost / AddQuantity。
    /// 無法計算（空欄位 / 0）時回 "0"。
    /// </summary>
    public string TxBuyComputedUnitPriceDisplay
    {
        get
        {
            if (!Buy.IsTotalMode)
                return "0";
            if (!ParseHelpers.TryParseDecimal(Buy.TotalCost, out var total) || total <= 0)
                return "0";
            if (!ParseHelpers.TryParseInt(AddAssetDialog.AddQuantity, out var qty) || qty <= 0)
                return "0";
            return (total / qty).ToString("N4").TrimEnd('0').TrimEnd('.');
        }
    }

    public bool CanFetchBuyFxRate => _transactionFxRateResolver is not null && Buy.IsCrossCurrency;

    [RelayCommand(CanExecute = nameof(CanFetchBuyFxRate))]
    private async Task FetchBuyFxRateAsync() => await RefreshBuyFxRateAsync(force: true).ConfigureAwait(true);

    private void QueueBuyFxRateRefresh()
    {
        _ = RefreshBuyFxRateAsync(force: false);
    }

    private async Task RefreshBuyFxRateAsync(bool force)
    {
        if (_transactionFxRateResolver is null || !TxTypeIsBuy)
            return;

        var instrumentCurrency = NormalizeCurrency(Buy.InstrumentCurrency);
        var settlementCurrency = NormalizeCurrency(Buy.SettlementCurrency, NormalizeCurrency(Buy.CashAccountCurrency));

        if (string.Equals(instrumentCurrency, settlementCurrency, StringComparison.OrdinalIgnoreCase))
        {
            SetResolvedBuyFx(string.Empty, null, string.Empty, isManual: false);
            Buy.FxFetchError = string.Empty;
            return;
        }

        if (Buy.IsFxManual && !force)
            return;

        Buy.FxFetchError = string.Empty;

        try
        {
            var quote = await _transactionFxRateResolver
                .ResolveAsync(DateOnly.FromDateTime(TxDate.Date), instrumentCurrency, settlementCurrency)
                .ConfigureAwait(true);

            if (!quote.IsAvailable || quote.Rate is not { } rate)
            {
                Buy.FxFetchError = L("Portfolio.Tx.FxRate.Unavailable", "查無此日期匯率，請手動輸入匯率或實際扣款金額。");
                return;
            }

            SetResolvedBuyFx(
                rate.ToString("0.########", CultureInfo.InvariantCulture),
                quote.RateDate,
                quote.Source ?? string.Empty,
                isManual: false);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to resolve transaction FX rate: {From}->{To} on {Date}",
                instrumentCurrency, settlementCurrency, TxDate.Date);
            Buy.FxFetchError = L("Portfolio.Tx.FxRate.Unavailable", "查無此日期匯率，請手動輸入匯率或實際扣款金額。");
        }
    }

    private void SetResolvedBuyFx(string rate, DateOnly? rateDate, string source, bool isManual)
    {
        _suppressBuyFxManualTracking = true;
        try
        {
            Buy.FxRate = rate;
            Buy.FxRateDate = rateDate;
            Buy.FxSourceLabel = source;
            Buy.IsFxManual = isManual;
        }
        finally
        {
            _suppressBuyFxManualTracking = false;
        }
    }

    private static string NormalizeCurrency(string? value, string fallback = "TWD") =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim().ToUpperInvariant();

    // ───────────────────────────────────────────────────────────────────
    // P5.8b prereq — Sell-side mirror of Buy's FX fetch infrastructure.
    // Same shape as Buy (lines 1492-1567):
    //   CanFetch / [RelayCommand] FetchAsync → forced refresh
    //   QueueXxxRefresh → fire-and-forget non-forced refresh used by handlers
    //   RefreshXxxFxRateAsync → real work, honors IsFxManual (skip unless force)
    //   SetResolvedXxxFx → write-back under suppress flag so PropertyChanged
    //     handler doesn't flip IsFxManual / FxSourceLabel during automated fills.
    // ───────────────────────────────────────────────────────────────────
    public bool CanFetchSellFxRate => _transactionFxRateResolver is not null && Sell.IsCrossCurrency;

    [RelayCommand(CanExecute = nameof(CanFetchSellFxRate))]
    private async Task FetchSellFxRateAsync() => await RefreshSellFxRateAsync(force: true).ConfigureAwait(true);

    private void QueueSellFxRateRefresh()
    {
        _ = RefreshSellFxRateAsync(force: false);
    }

    private async Task RefreshSellFxRateAsync(bool force)
    {
        if (_transactionFxRateResolver is null || !TxTypeIsSell)
            return;

        var instrumentCurrency = NormalizeCurrency(Sell.InstrumentCurrency);
        var settlementCurrency = NormalizeCurrency(Sell.SettlementCurrency, NormalizeCurrency(Sell.CashAccountCurrency));

        if (string.Equals(instrumentCurrency, settlementCurrency, StringComparison.OrdinalIgnoreCase))
        {
            SetResolvedSellFx(string.Empty, null, string.Empty, isManual: false);
            Sell.FxFetchError = string.Empty;
            return;
        }

        if (Sell.IsFxManual && !force)
            return;

        Sell.FxFetchError = string.Empty;

        try
        {
            var quote = await _transactionFxRateResolver
                .ResolveAsync(DateOnly.FromDateTime(TxDate.Date), instrumentCurrency, settlementCurrency)
                .ConfigureAwait(true);

            if (!quote.IsAvailable || quote.Rate is not { } rate)
            {
                Sell.FxFetchError = L("Portfolio.Tx.FxRate.Unavailable", "查無此日期匯率，請手動輸入匯率或實際扣款金額。");
                return;
            }

            SetResolvedSellFx(
                rate.ToString("0.########", CultureInfo.InvariantCulture),
                quote.RateDate,
                quote.Source ?? string.Empty,
                isManual: false);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to resolve transaction FX rate: {From}->{To} on {Date}",
                instrumentCurrency, settlementCurrency, TxDate.Date);
            Sell.FxFetchError = L("Portfolio.Tx.FxRate.Unavailable", "查無此日期匯率，請手動輸入匯率或實際扣款金額。");
        }
    }

    private void SetResolvedSellFx(string rate, DateOnly? rateDate, string source, bool isManual)
    {
        _suppressSellFxManualTracking = true;
        try
        {
            Sell.FxRate = rate;
            Sell.FxRateDate = rateDate;
            Sell.FxSourceLabel = source;
            Sell.IsFxManual = isManual;
        }
        finally
        {
            _suppressSellFxManualTracking = false;
        }
    }

    // ───────────────────────────────────────────────────────────────────
    // P5.8b prereq — Dividend-side mirror (same pattern as Sell + Buy).
    // Note gate: TxTypeIsCashDiv — stock dividends have no cash flow so no FX.
    // ───────────────────────────────────────────────────────────────────
    public bool CanFetchDividendFxRate => _transactionFxRateResolver is not null && Div.IsCrossCurrency;

    [RelayCommand(CanExecute = nameof(CanFetchDividendFxRate))]
    private async Task FetchDividendFxRateAsync() => await RefreshDividendFxRateAsync(force: true).ConfigureAwait(true);

    private void QueueDividendFxRateRefresh()
    {
        _ = RefreshDividendFxRateAsync(force: false);
    }

    private async Task RefreshDividendFxRateAsync(bool force)
    {
        if (_transactionFxRateResolver is null || !TxTypeIsCashDiv)
            return;

        var instrumentCurrency = NormalizeCurrency(Div.InstrumentCurrency);
        var settlementCurrency = NormalizeCurrency(Div.SettlementCurrency, NormalizeCurrency(Div.CashAccountCurrency));

        if (string.Equals(instrumentCurrency, settlementCurrency, StringComparison.OrdinalIgnoreCase))
        {
            SetResolvedDividendFx(string.Empty, null, string.Empty, isManual: false);
            Div.FxFetchError = string.Empty;
            return;
        }

        if (Div.IsFxManual && !force)
            return;

        Div.FxFetchError = string.Empty;

        try
        {
            var quote = await _transactionFxRateResolver
                .ResolveAsync(DateOnly.FromDateTime(TxDate.Date), instrumentCurrency, settlementCurrency)
                .ConfigureAwait(true);

            if (!quote.IsAvailable || quote.Rate is not { } rate)
            {
                Div.FxFetchError = L("Portfolio.Tx.FxRate.Unavailable", "查無此日期匯率，請手動輸入匯率或實際扣款金額。");
                return;
            }

            SetResolvedDividendFx(
                rate.ToString("0.########", CultureInfo.InvariantCulture),
                quote.RateDate,
                quote.Source ?? string.Empty,
                isManual: false);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to resolve transaction FX rate: {From}->{To} on {Date}",
                instrumentCurrency, settlementCurrency, TxDate.Date);
            Div.FxFetchError = L("Portfolio.Tx.FxRate.Unavailable", "查無此日期匯率，請手動輸入匯率或實際扣款金額。");
        }
    }

    private void SetResolvedDividendFx(string rate, DateOnly? rateDate, string source, bool isManual)
    {
        _suppressDividendFxManualTracking = true;
        try
        {
            Div.FxRate = rate;
            Div.FxRateDate = rateDate;
            Div.FxSourceLabel = source;
            Div.IsFxManual = isManual;
        }
        finally
        {
            _suppressDividendFxManualTracking = false;
        }
    }

    /// <summary>
    /// React to Buy.X changes — runs side effects that the old <c>partial OnTxBuy*Changed</c>
    /// handlers performed (UpdateBuyPreview, write-back AddPrice in total mode, validation).
    /// XAML now binds directly to Buy.X so no facade re-notification needed.
    /// </summary>
    private void OnBuyPriceModeChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(BuyTxViewModel.PriceMode):
                // 三欄連動後不再需要「切到總額模式時回填總額」——成交總額欄位一直可見且持續同步，
                // 不會有空欄要補。PriceMode 現在只是內部權威旗標，由使用者輸入哪一欄決定。
                OnPropertyChanged(nameof(TxBuyComputedTotalDisplay));
                OnPropertyChanged(nameof(TxBuyComputedUnitPriceDisplay));
                break;
            case nameof(BuyTxViewModel.TotalCost):
                // _suppressBuyPriceRewrite 為 true 代表這是 AddPrice→TotalCost 的程式回寫，非使用者輸入。
                if (!_suppressBuyPriceRewrite)
                {
                    // 使用者親手打「成交總額」→ 總額為權威，單價改由總額反推。
                    Buy.PriceMode = "total";
                    RecomputeBuyDerivedAmount();
                }
                Buy.TotalCostError = ValidatePositiveDecimalOrEmpty(Buy.TotalCost);
                OnPropertyChanged(nameof(TxBuyComputedTotalDisplay));
                OnPropertyChanged(nameof(TxBuyComputedUnitPriceDisplay));
                break;
            case nameof(BuyTxViewModel.TotalIncludesFee):
                AddAssetDialog.UpdateBuyPreview();
                OnPropertyChanged(nameof(TxBuyComputedTotalDisplay));
                OnPropertyChanged(nameof(TxBuyComputedUnitPriceDisplay));
                break;
            case nameof(BuyTxViewModel.ActualCashAmount):
                Buy.ActualCashAmountError = ValidatePositiveDecimalOrEmpty(Buy.ActualCashAmount);
                NotifyImpactPreviewChanged();
                break;
            case nameof(BuyTxViewModel.FxRate):
                // P3 — same validation pattern as ActualCashAmount; empty allowed (= implicit 1.0).
                Buy.FxRateError = ValidatePositiveDecimalOrEmpty(Buy.FxRate);
                if (!_suppressBuyFxManualTracking)
                {
                    Buy.IsFxManual = !string.IsNullOrWhiteSpace(Buy.FxRate);
                    if (Buy.IsFxManual)
                    {
                        Buy.FxSourceLabel = "manual";
                        Buy.FxRateDate ??= DateOnly.FromDateTime(TxDate.Date);
                    }
                }
                NotifyImpactPreviewChanged();
                break;
            case nameof(BuyTxViewModel.InstrumentCurrency):
            case nameof(BuyTxViewModel.CashAccountCurrency):
            case nameof(BuyTxViewModel.SettlementCurrency):
                FetchBuyFxRateCommand.NotifyCanExecuteChanged();
                QueueBuyFxRateRefresh();
                NotifyImpactPreviewChanged();
                break;
            case nameof(BuyTxViewModel.SettlementInputMode):
                NotifyImpactPreviewChanged();
                break;
        }
    }

    // CashDividend / StockDividend 輸入模式 + 驗證 — 全部移到 Div (DividendTxViewModel).
    // 由 OnDividendTxChanged 處理 PropertyChanged 觸發的驗證與重算。

    /// <summary>
    /// React to Transfer.X / Loan.X PropertyChanged: validation + auto-fill +
    /// liability-balance preview. Replaces the old partial OnTxXxxChanged handlers.
    /// </summary>
    private void OnTransferTxChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(TransferTxViewModel.TargetAmount):
                Transfer.TargetAmountError = ValidatePositiveDecimalOrEmpty(Transfer.TargetAmount);
                break;
        }
    }

    private void OnLoanTxChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(LoanTxViewModel.Label):
                if (TxTypeIsLoanRepay)
                    _ = AutoFillLoanRepayAsync(Loan.Label);
                NotifyImpactPreviewChanged();
                break;
            case nameof(LoanTxViewModel.Principal):
                Loan.PrincipalError = ValidatePositiveDecimalOrEmpty(Loan.Principal);
                NotifyImpactPreviewChanged();
                break;
            case nameof(LoanTxViewModel.InterestPaid):
                Loan.InterestPaidError = ValidateNonNegativeDecimalOrEmpty(Loan.InterestPaid);
                NotifyImpactPreviewChanged();
                break;
            case nameof(LoanTxViewModel.Rate):
                Loan.RateError = ValidateNonNegativeDecimalOrEmpty(Loan.Rate);
                break;
            case nameof(LoanTxViewModel.TermMonths):
                Loan.TermMonthsError = string.IsNullOrWhiteSpace(Loan.TermMonths) ? string.Empty
                    : (ParseHelpers.TryParseInt(Loan.TermMonths, out var n) && n > 0 ? string.Empty : "請輸入正整數");
                break;
        }
    }

    // ── 新增紀錄 Dialog commands ──────────────────────────────────────────────────────

    internal void OpenTxDialog(Guid? preferredGroupId = null)
    {
        IsRevisionMode = false;
        IsAssetContextLocked = false;
        var state = _tradeDialogController.CreateOpenState(_getDefaultCashAccount());
        EditingTradeId = null;
        TxLoanScheduleEntryId = null;
        TxType = "buy";
        TxDate = state.TxDate;
        TxError = string.Empty;
        TxAmountError = string.Empty;
        TxFeeError = string.Empty;
        Div.PerShareError = string.Empty;
        Div.TotalInputError = string.Empty;
        Div.StockNewSharesError = string.Empty;
        Transfer.TargetAmountError = string.Empty;
        Loan.PrincipalError = string.Empty;
        Loan.InterestPaidError = string.Empty;
        Buy.TotalCostError = string.Empty;
        Buy.ActualCashAmountError = string.Empty;
        TxCommissionDiscountError = string.Empty;
        // 從 AppSettings.DefaultCommissionDiscount 帶預設值 — 取代過去的硬碼 "1.0"。
        SeedCommissionDiscountFromSettings();
        // P2.2 — 新 dialog 一開始重置「使用者改過 currency」標記，否則上次手動改過會延續。
        SelectedAsset = null;
        _userTouchedCurrency = false;
        // Q02 — 非編輯路徑（含 OpenTxDialogForPosition/Liability 先呼叫本方法）清掉編輯 row，
        // 確保 BuildAvailableAssets 不會殘留上次編輯時注入的合成 subject。
        _editingTradeRow = null;
        // P2.7 — 上次 dialog 殘留的搜尋字串可能誤過濾掉這次的資產，先清掉再 rebuild view。
        AssetSearchText = string.Empty;
        // 資產清單可能在上次 dialog 期間有變動（新增持倉 / 帳戶 / 負債）— 重建 cache + grouped view。
        InvalidateAvailableAssetsCache();
        TxAmount = string.Empty;
        TxNote = string.Empty;
        _suppressCategoryAutoTracking = true;
        try
        { TxCategoryId = null; }
        finally { _suppressCategoryAutoTracking = false; }
        _txCategoryAutoMatched = false;
        // 新增（非編輯）時帶入使用者在現金頁設定的預設帳戶
        TxCashAccount = state.TxCashAccount;
        Div.Position = null;
        Div.PerShare = string.Empty;
        Div.Total = 0;
        Div.StockPosition = null;
        Div.StockNewShares = string.Empty;
        Loan.Label = string.Empty;
        TxFee = string.Empty;
        TxUseCashAccount = state.TxUseCashAccount;
        SyncTxCashAccountNameFromSelection(TxCashAccount);
        Transfer.Target = null;
        Transfer.TargetName = string.Empty;
        Transfer.TargetAmount = string.Empty;
        CreditCard.Card = null;
        Buy.AssetType = state.TxBuyAssetType;
        Buy.PriceMode = state.TxBuyPriceMode;
        Buy.TotalCost = string.Empty;
        Buy.TotalIncludesFee = true;  // Reset to default — most broker totals include fee.
        Buy.ActualCashAmount = string.Empty;
        Buy.FxRate = string.Empty;
        Buy.FxRateDate = null;
        Buy.FxSourceLabel = string.Empty;
        Buy.IsFxManual = false;
        Buy.FxFetchError = string.Empty;
        Buy.CashAccountCurrency = TxCashAccount?.Currency ?? string.Empty;
        Buy.SettlementCurrency = TxCashAccount?.Currency ?? string.Empty;
        Buy.MetaOnly = false;
        Div.InputMode = state.TxDivInputMode;
        Div.TotalInput = string.Empty;
        TxCommissionDiscount = state.TxCommissionDiscount;
        Sell.Position = null;
        Sell.Quantity = string.Empty;
        Sell.QuantityError = string.Empty;
        EditSummaryType = string.Empty;
        EditSummaryTarget = string.Empty;
        EditSummaryAmount = string.Empty;
        EditSummaryQuantity = string.Empty;
        Loan.Principal = string.Empty;
        Loan.InterestPaid = string.Empty;
        Loan.Rate = string.Empty;
        Loan.TermMonths = string.Empty;
        Loan.StartDate = state.TxLoanStartDate;
        Loan.RateError = string.Empty;
        Loan.TermMonthsError = string.Empty;
        // Buy-specific fields shared with AddAssetDialog sub-VM
        AddAssetDialog.AddSymbol = string.Empty;
        AddAssetDialog.AddPrice = string.Empty;
        AddAssetDialog.AddQuantity = string.Empty;
        AddAssetDialog.AddBuyDate = state.AddBuyDate;
        AddAssetDialog.AddError = string.Empty;
        AddAssetDialog.AddName = string.Empty;
        AddAssetDialog.AddCost = string.Empty;
        AddAssetDialog.AddCryptoSymbol = string.Empty;
        AddAssetDialog.AddCryptoQty = string.Empty;
        AddAssetDialog.AddCryptoPrice = string.Empty;
        AddAssetDialog.AddCreditCardName = string.Empty;
        AddAssetDialog.AddCreditCardIssuer = string.Empty;
        AddAssetDialog.AddCreditCardBillingDay = string.Empty;
        AddAssetDialog.AddCreditCardDueDay = string.Empty;
        AddAssetDialog.AddCreditCardLimit = string.Empty;
        SellPanel.SellCashAccount = null;
        SellPanel.SellPriceInput = string.Empty;
        AddAssetDialog.AddPriceError = string.Empty;
        AddAssetDialog.AddQuantityError = string.Empty;
        AddAssetDialog.AddCostError = string.Empty;
        AddAssetDialog.AddCryptoQtyError = string.Empty;
        AddAssetDialog.AddCryptoPriceError = string.Empty;
        // Portfolio-Groups-Refactor P3 — 開新交易時刷新 catalog 並預設選 DefaultGroup。
        _ = EnsureGroupsLoadedAsync(restoreFromEditTradeId: null, preferredGroupId);
        IsTxDialogOpen = true;
    }

    internal void OpenTxDialogForPosition(PortfolioRowViewModel row, string txType)
    {
        OpenTxDialog(row.PortfolioGroupId);

        var asset = ResolveAssetSubjectForPosition(row);
        if (asset is not null)
        {
            SelectedAsset = asset;
            IsAssetContextLocked = true;
        }

        TxType = string.IsNullOrWhiteSpace(txType) ? "buy" : txType;

        switch (TxType)
        {
            case "sell":
                Sell.Position = row;
                Sell.Quantity = ((int)row.Quantity).ToString();
                break;
            case "cashDiv":
                Div.Position = row;
                break;
            case "stockDiv":
                Div.StockPosition = row;
                break;
            case "buy":
                Buy.AssetType = row.AssetType switch
                {
                    Core.Models.AssetType.Fund => "fund",
                    Core.Models.AssetType.Crypto => "crypto",
                    Core.Models.AssetType.PreciousMetal => "metal",
                    Core.Models.AssetType.Bond => "bond",
                    _ => "stock",
                };
                AddAssetDialog.AddSymbol = row.Symbol;
                AddAssetDialog.AddPrice = string.Empty;
                AddAssetDialog.AddQuantity = string.Empty;
                break;
        }
    }

    internal void OpenTxDialogForLiability(LiabilityRowViewModel row, string txType)
    {
        OpenTxDialog();

        var asset = ResolveAssetSubjectForLiability(row);
        if (asset is not null)
        {
            SelectedAsset = asset;
            IsAssetContextLocked = true;
        }

        if (row.IsCreditCard)
            CreditCard.Card = row;
        else
            Loan.Label = row.Label;

        TxType = string.IsNullOrWhiteSpace(txType)
            ? row.IsCreditCard ? "creditCardPayment" : "loanRepay"
            : txType;
    }

    /// <summary>
    /// Q05 — Cash detail panel「+ 新增交易」用：開啟 Tx 對話框、預選對應的資金帳戶
    /// subject 並鎖定統一資產選擇器（隱藏冗餘 picker），同時預填 TxCashAccount + TxType。
    /// transfer 時鎖定來源帳戶與編輯模式一致；轉帳目標帳戶選擇器（Transfer.Target）
    /// 由 TxTypeIsTransfer 控制顯示，與 ShowAssetSelector 無關，故鎖定後仍可選目標。
    /// </summary>
    internal void OpenTxDialogForCashAccount(CashAccountRowViewModel row, string txType)
    {
        OpenTxDialog();

        var asset = ResolveAssetSubjectForCashAccount(row);
        if (asset is not null)
        {
            SelectedAsset = asset;
            IsAssetContextLocked = true;
        }

        TxCashAccount = row;
        TxType = string.IsNullOrWhiteSpace(txType) ? "deposit" : txType;
    }

    internal void OpenTxDialogForLoanSchedule(LiabilityRowViewModel row, LoanScheduleRowViewModel entry)
    {
        OpenTxDialogForLiability(row, "loanRepay");

        TxLoanScheduleEntryId = entry.Id;
        TxDate = entry.DueDate.ToDateTime(TimeOnly.MinValue);
        Loan.Principal = entry.PrincipalAmount.ToString("F0");
        Loan.InterestPaid = entry.InterestAmount > 0
            ? entry.InterestAmount.ToString("F0")
            : string.Empty;
        TxUseCashAccount = true;
    }

    /// <summary>
    /// Portfolio-Groups-Refactor P3 — 確保 group catalog 已載入，並依情境設定
    /// <see cref="SelectedPortfolioGroup"/>。restoreFromEditTradeId 指定時試圖
    /// 從 trade row 還原；否則預設為 DefaultGroup。
    /// </summary>
    private async Task EnsureGroupsLoadedAsync(Guid? restoreFromEditTradeId, Guid? preferredGroupId = null)
    {
        if (GroupCatalog is null)
            return;
        try
        {
            await GroupCatalog.EnsureLoadedAsync().ConfigureAwait(true);
            OnPropertyChanged(nameof(IsGroupSelectorVisible));
            OnPropertyChanged(nameof(ShouldShowGroupSelector));

            Guid? targetId = preferredGroupId;
            if (restoreFromEditTradeId is { } editId)
            {
                var trade = Trades.FirstOrDefault(t => t.Id == editId);
                targetId = trade?.PortfolioGroupId ?? preferredGroupId;
            }
            SelectedPortfolioGroup = GroupCatalog.FindById(targetId) ?? GroupCatalog.Default;
        }
        catch
        {
            // 群組載入失敗時不阻斷 dialog；ComboBox 維持空。
        }
    }

    [RelayCommand]
    private void EditTrade(TradeRowViewModel row)
    {
        IsRevisionMode = false;
        IsAssetContextLocked = false;
        TxLoanScheduleEntryId = null;
        // Q02 — 記住正在編輯的 row，讓接下來的 InvalidateAvailableAssetsCache → BuildAvailableAssets
        // 在標的不在 live view 時併入合成 subject（必須在 invalidate 之前設，rebuild 才會包含它）。
        _editingTradeRow = row;
        // P2.7 — 編輯模式不需要殘留的搜尋字串干擾 picker。
        AssetSearchText = string.Empty;
        // 資產清單可能在 dialog 上次關閉後有變動 — 進編輯模式前也重建 cache 一次，
        // 避免 ResolveAssetSubjectForTrade 命中 stale list 找不到剛改名的持倉。
        InvalidateAvailableAssetsCache();
        var editState = _tradeDialogController.CreateEditState(row, Positions, CashAccounts, Liabilities);
        EditingTradeId = editState.EditingTradeId;
        PopulateEditSummary(row);
        TxDate = editState.TxDate;
        TxError = string.Empty;
        TxAmountError = string.Empty;
        TxFeeError = string.Empty;
        Div.PerShareError = string.Empty;
        Div.TotalInputError = string.Empty;
        Div.StockNewSharesError = string.Empty;
        Transfer.TargetAmountError = string.Empty;
        Loan.PrincipalError = string.Empty;
        Loan.InterestPaidError = string.Empty;
        Buy.TotalCostError = string.Empty;
        Buy.ActualCashAmountError = string.Empty;
        TxCommissionDiscountError = string.Empty;
        TxNote = editState.TxNote;

        // Reset every type-specific field first so state from a previous open doesn't bleed
        // through (e.g. editing Income after editing Buy would otherwise leave AddSymbol set).
        TxAmount = string.Empty;
        TxCashAccount = null;
        TxCashAccountName = string.Empty;
        Loan.Label = string.Empty;
        TxFee = string.Empty;
        TxUseCashAccount = true;
        Transfer.Target = null;
        Transfer.TargetName = string.Empty;
        Transfer.TargetAmount = string.Empty;
        CreditCard.Card = null;
        Loan.Principal = string.Empty;
        Loan.InterestPaid = string.Empty;
        Div.Position = null;
        Div.PerShare = string.Empty;
        Div.StockPosition = null;
        Div.StockNewShares = string.Empty;
        Sell.Position = null;
        Sell.Quantity = string.Empty;
        Sell.QuantityError = string.Empty;
        SellPanel.SellCashAccount = null;
        SellPanel.SellPriceInput = string.Empty;
        AddAssetDialog.AddSymbol = string.Empty;
        AddAssetDialog.AddPrice = string.Empty;
        AddAssetDialog.AddQuantity = string.Empty;
        AddAssetDialog.AddPriceError = string.Empty;
        AddAssetDialog.AddQuantityError = string.Empty;
        AddAssetDialog.AddCostError = string.Empty;
        AddAssetDialog.AddCryptoQtyError = string.Empty;
        AddAssetDialog.AddCryptoPriceError = string.Empty;
        AddAssetDialog.AddCreditCardName = string.Empty;
        AddAssetDialog.AddCreditCardIssuer = string.Empty;
        AddAssetDialog.AddCreditCardBillingDay = string.Empty;
        AddAssetDialog.AddCreditCardDueDay = string.Empty;
        AddAssetDialog.AddCreditCardLimit = string.Empty;
        SeedCommissionDiscountFromSettings();
        Buy.Reset();
        Div.InputMode = "perShare";
        Div.TotalInput = string.Empty;

        // P5.16 — Reorder: SelectedAsset BEFORE TxType。
        //   AvailableTradeTypes 是依 SelectedAsset.Kind 過濾的（line 309-317）。
        //   若先 TxType = "creditCardCharge"，此時 SelectedAsset=null →
        //   AvailableTradeTypes=[] → ComboBox 找不到 "creditCardCharge" 對應 item →
        //   類型 picker 顯示「請選擇類型」placeholder（即使 TxType 值已正確）。
        //   反過來：先 SelectedAsset = liability → AvailableTradeTypes 含 4 個信用卡/貸款
        //   type → 再設 TxType ComboBox 能正確 match。
        //   OnSelectedAssetChanged 第 3 步會做 type 合法性 fallback，但我們緊接著 override，
        //   不影響最終值。
        //   _userTouchedCurrency 設為 true 以避免下方的 cascade 把使用者原本記錄的 currency 蓋掉。
        _userTouchedCurrency = true;
        SelectedAsset = ResolveAssetSubjectForTrade(row);
        TxType = editState.TxType;
        CreditCard.Card = editState.TxCreditCard;

        switch (row.Type)
        {
            case TradeType.Buy:
                // Full edit path when linked to a lot; meta-only otherwise (dialog locks fields).
                // Suppress the symbol-search suggestions popup during pre-fill — the popup
                // exists for live search-as-you-type, not for showing what the user is
                // already editing.
                AddAssetDialog.SuppressSuggestions = true;
                AddAssetDialog.SuppressClosePriceAutoFill = true;
                AddAssetDialog.AddSymbol = editState.AddSymbol;
                AddAssetDialog.AddPrice = editState.AddPrice;
                AddAssetDialog.AddQuantity = editState.AddQuantity;
                AddAssetDialog.AddBuyDate = editState.AddBuyDate ?? TxDate;
                AddAssetDialog.SuppressSuggestions = false;
                AddAssetDialog.SuppressClosePriceAutoFill = false;
                AddAssetDialog.IsSuggestionsOpen = false;
                TxCashAccount = editState.TxCashAccount;
                TxUseCashAccount = editState.TxUseCashAccount;
                Buy.ActualCashAmount = editState.TxActualCashAmount;
                // MultiCurrency-Trade-Refactor P3 — restore cross-currency FX rate + currency
                // so the banner + FxRate field re-appear when reopening a cross-currency Buy.
                // InstrumentCurrency must be set explicitly because the autocomplete chain
                // clears AddSymbolCurrency on AddSymbol set, and TxCashAccount push wires the
                // funding side only.
                _suppressBuyFxManualTracking = true;
                try
                {
                    Buy.FxRate = editState.TxFxRate;
                }
                finally
                {
                    _suppressBuyFxManualTracking = false;
                }
                if (!string.IsNullOrWhiteSpace(editState.TxInstrumentCurrency))
                    Buy.InstrumentCurrency = editState.TxInstrumentCurrency;
                Buy.SettlementCurrency = !string.IsNullOrWhiteSpace(editState.TxSettlementCurrency)
                    ? editState.TxSettlementCurrency
                    : editState.TxCashAccount?.Currency ?? string.Empty;
                Buy.FxRateDate = editState.TxFxRateDate;
                Buy.FxSourceLabel = editState.TxFxSource ?? string.Empty;
                Buy.IsFxManual = string.Equals(editState.TxFxSource, "manual", StringComparison.OrdinalIgnoreCase);
                // Infer the original "金額已含手續費" state from the recorded
                // Commission. A trade saved via the new total-mode-with-fee
                // path has Commission == 0 (or null); a trade that went through
                // the old fee-calculator path has Commission > 0. Without this
                // inference, EditTrade's blanket reset to true would silently
                // strip commission from the recreated trade.
                Buy.TotalIncludesFee = (row.Commission ?? 0m) <= 0m;
                // 還原使用者當初輸入的手續費來源：
                //   CommissionDiscount 有值  → 走折扣路徑 → 帶回折扣，TxFee 留空
                //   CommissionDiscount null 但 Commission 有值 → 走手動覆蓋 → 帶回 TxFee
                //   兩者皆 null（legacy） → 折扣維持預設 1.0，TxFee 空
                RestoreCommissionFields(row);
                break;

            case TradeType.Sell:
                // Meta-only edit: show the sell values for reference; date/note are the only
                // editable fields. The position itself was closed at sell time.
                // Populate BOTH TxCashAccount (dialog XAML binds to this) AND SellCashAccount
                // (ConfirmSell uses this). Without the former, the dialog dropdown was empty.
                Sell.SuppressPositionPriceAutoFill = true;
                Sell.Position = editState.TxSellPosition;
                Sell.SuppressPositionPriceAutoFill = false;
                Sell.Quantity = editState.TxSellQuantity;
                TxAmount = editState.TxAmount;
                SellPanel.SellPriceInput = editState.SellPriceInput;
                TxCashAccount = editState.TxCashAccount;
                SellPanel.SellCashAccount = editState.SellCashAccount;
                TxUseCashAccount = editState.TxUseCashAccount;
                // P3 — restore cross-currency state. InstrumentCurrency falls back to the
                // Position-derived value via OnSellTxChanged when Sell.Position is assigned,
                // but explicit set wins to handle Trade rows for de-listed / archived positions.
                if (!string.IsNullOrWhiteSpace(editState.TxInstrumentCurrency))
                    Sell.InstrumentCurrency = editState.TxInstrumentCurrency;
                // Same gate as CashDividend below — only restore CashAmount overrides for
                // genuinely cross-currency sells, so same-currency revisions can re-derive.
                var sellCashCcy = editState.TxCashAccount?.Currency;
                var sellIsCrossCcy =
                    !string.IsNullOrWhiteSpace(editState.TxFxRate)
                    || (!string.IsNullOrWhiteSpace(editState.TxInstrumentCurrency)
                        && !string.IsNullOrWhiteSpace(sellCashCcy)
                        && !string.Equals(editState.TxInstrumentCurrency, sellCashCcy, StringComparison.OrdinalIgnoreCase));
                if (sellIsCrossCcy)
                {
                    Sell.ActualCashAmount = editState.TxActualCashAmount;
                    // P5.8b prereq — wrap with suppress flag so OnSellTxChanged's
                    // FxRate handler doesn't falsely flip IsFxManual when
                    // restoring a previously-fetched (non-manual) FX rate.
                    _suppressSellFxManualTracking = true;
                    try
                    {
                        Sell.FxRate = editState.TxFxRate;
                    }
                    finally
                    {
                        _suppressSellFxManualTracking = false;
                    }
                    Sell.SettlementCurrency = !string.IsNullOrWhiteSpace(editState.TxSettlementCurrency)
                        ? editState.TxSettlementCurrency
                        : sellCashCcy ?? string.Empty;
                    Sell.FxRateDate = editState.TxFxRateDate;
                    Sell.FxSourceLabel = editState.TxFxSource ?? string.Empty;
                    Sell.IsFxManual = string.Equals(editState.TxFxSource, "manual", StringComparison.OrdinalIgnoreCase);
                }
                RestoreCommissionFields(row);
                UpdateSellTxPreview();
                break;

            case TradeType.CashDividend:
                Div.Position = editState.TxDivPosition;
                Div.PerShare = editState.TxDivPerShare;
                TxCashAccount = editState.TxCashAccount;
                TxUseCashAccount = editState.TxUseCashAccount;
                if (!string.IsNullOrWhiteSpace(editState.TxInstrumentCurrency))
                    Div.InstrumentCurrency = editState.TxInstrumentCurrency;
                // P3 — restore cross-currency overrides ONLY when the trade actually was
                // cross-currency. For same-currency dividends, CashAmount on disk is just
                // (PerShare × Qty); restoring it would prevent revision from re-deriving
                // a new CashAmount when the user changes PerShare. Detection: FxRate
                // recorded → definitely cross-currency; otherwise compare instrument vs
                // cash account currency.
                var divCashCcy = editState.TxCashAccount?.Currency;
                var divIsCrossCcy =
                    !string.IsNullOrWhiteSpace(editState.TxFxRate)
                    || (!string.IsNullOrWhiteSpace(editState.TxInstrumentCurrency)
                        && !string.IsNullOrWhiteSpace(divCashCcy)
                        && !string.Equals(editState.TxInstrumentCurrency, divCashCcy, StringComparison.OrdinalIgnoreCase));
                if (divIsCrossCcy)
                {
                    Div.ActualCashAmount = editState.TxActualCashAmount;
                    // P5.8b prereq — same suppress-wrap as Sell.
                    _suppressDividendFxManualTracking = true;
                    try
                    {
                        Div.FxRate = editState.TxFxRate;
                    }
                    finally
                    {
                        _suppressDividendFxManualTracking = false;
                    }
                    Div.SettlementCurrency = !string.IsNullOrWhiteSpace(editState.TxSettlementCurrency)
                        ? editState.TxSettlementCurrency
                        : divCashCcy ?? string.Empty;
                    Div.FxRateDate = editState.TxFxRateDate;
                    Div.FxSourceLabel = editState.TxFxSource ?? string.Empty;
                    Div.IsFxManual = string.Equals(editState.TxFxSource, "manual", StringComparison.OrdinalIgnoreCase);
                }
                break;

            case TradeType.StockDividend:
                Div.StockPosition = editState.TxStockDivPosition;
                Div.StockNewShares = editState.TxStockDivNewShares;
                break;

            case TradeType.Income:
            case TradeType.Deposit:
            case TradeType.Withdrawal:
                TxAmount = editState.TxAmount;
                TxCashAccount = editState.TxCashAccount;
                break;

            case TradeType.LoanBorrow:
                TxAmount = editState.TxAmount;
                Loan.Label = editState.TxLoanLabel;
                TxCashAccount = editState.TxCashAccount;
                TxUseCashAccount = editState.TxUseCashAccount;
                TxFee = string.Empty;
                break;

            case TradeType.LoanRepay:
                // CashAmount = Principal + InterestPaid（向後相容：Principal null 代表全額為本金）
                Loan.Principal = editState.TxPrincipal;
                Loan.InterestPaid = editState.TxInterestPaid;
                Loan.Label = editState.TxLoanLabel;
                TxCashAccount = editState.TxCashAccount;
                TxUseCashAccount = editState.TxUseCashAccount;
                // Fee can't be inferred from trade record alone; leave blank.
                TxFee = string.Empty;
                break;

            case TradeType.Transfer:
                // Direct edit (since 2026-05 refactor): all fields shown editable. On Save,
                // ConfirmTx falls through to ConfirmTransferAsync which records a new
                // single-record (same amount) or paired (cross-currency) transfer, and the
                // post-success block deletes the original single Transfer trade. Cross-currency
                // LEG edits remain meta-only (IsTransferLeg covers that).
                TxAmount = row.CashAmount?.ToString("F0") ?? string.Empty;
                Transfer.TargetAmount = row.CashAmount?.ToString("F0") ?? string.Empty;
                TxCashAccount = row.CashAccountId is { } txSrcAcc
                    ? CashAccounts.FirstOrDefault(c => c.Id == txSrcAcc) : null;
                Transfer.Target = row.ToCashAccountId is { } txDstAcc
                    ? CashAccounts.FirstOrDefault(c => c.Id == txDstAcc) : null;
                // 編輯既有轉帳時鎖定來源：上方「選擇資產」改為唯讀摘要，避免不小心把這筆轉帳的來源
                // 改成別的帳戶（要換來源請刪除後重記）。金額／轉入帳戶／日期等其餘欄位仍可編輯。
                IsAssetContextLocked = true;
                break;

            // P5.16 — 補上信用卡兩個 type 的 restore case。原本 switch 漏這兩個 case
            // → 編輯紀錄 modal 開起來欄位全空白（CreditCard.Card 是 switch 之外設定的
            // 所以信用卡 picker 還有值，但 TxAmount / TxCashAccount 維持 reset 後的空字串
            // 跟 null）。Controller 已在 editState 把 TxAmount / TxCashAccount 帶好。
            //   Charge:  conform 用 TxAmount + CreditCard.Card + TxNote (不用 cash 帳戶)
            //   Payment: conform 用 TxAmount + CreditCard.Card + TxCashAccount + TxNote
            // 兩個 type 統一 restore，多塞的 TxCashAccount 對 Charge confirm 無影響。
            case TradeType.CreditCardCharge:
            case TradeType.CreditCardPayment:
                TxAmount = editState.TxAmount;
                TxCashAccount = editState.TxCashAccount;
                TxUseCashAccount = editState.TxUseCashAccount;
                break;
        }

        // Portfolio-Groups-Refactor P3 — 還原 SelectedPortfolioGroup 自編輯目標 trade 的 PortfolioGroupId。
        _ = EnsureGroupsLoadedAsync(restoreFromEditTradeId: row.Id);
        IsTxDialogOpen = true;
    }

    /// <summary>
    /// 依據被編輯交易的 <see cref="TradeRowViewModel.CommissionDiscount"/> 與
    /// <see cref="TradeRowViewModel.Commission"/> 欄位，還原「手續費折扣」/「手續費（選填）」
    /// 的 UI 狀態，避免折扣與手動覆蓋兩條路徑互相污染。
    /// </summary>
    private void RestoreCommissionFields(TradeRowViewModel row)
    {
        if (row.CommissionDiscount is { } disc && disc > 0)
        {
            // 折扣路徑：還原折扣值，TxFee 留空避免誤觸發手動覆蓋
            TxCommissionDiscount = disc.ToString("0.##");
            TxFee = string.Empty;
        }
        else if (row.Commission is { } com && com > 0)
        {
            // 手動覆蓋路徑：還原 TxFee，折扣維持前面 EditTrade 設定的 "1.0" 預設
            TxFee = Math.Round(com, 0).ToString("F0");
        }
        else
        {
            // Commission == 0 / null（「成交總額已含手續費」的原始輸入，或 legacy 無手續費）：
            // 明確設 TxFee = "0"。否則每股價格模式會用「折扣 × 標準費率」重新估一筆手續費，
            // 把編輯前的總成本灌大（編輯既有交易應保留原記錄金額，不重算手續費）。
            TxFee = "0";
        }
    }

    [RelayCommand]
    private void CloseTxDialog()
    {
        IsRevisionMode = false;
        EditingTradeId = null;
        if (!_preserveRevisionSourceOnClose)
        {
            _revisionSourceTradeId = null;
            IsRevisionReplacePromptOpen = false;
            RevisionReplacePromptError = string.Empty;
        }
        EditSummaryType = string.Empty;
        EditSummaryTarget = string.Empty;
        EditSummaryAmount = string.Empty;
        EditSummaryQuantity = string.Empty;
        IsTxDialogOpen = false;
    }



    private string L(string key, string fallback = "") => _localize(key, fallback);

    private void PopulateEditSummary(TradeRowViewModel row)
    {
        EditSummaryType = row.Type switch
        {
            TradeType.Buy => L("Portfolio.TradeType.Buy", "買入"),
            TradeType.Sell => L("Portfolio.TradeType.Sell", "賣出"),
            TradeType.Income => L("Portfolio.TradeType.Income", "收入"),
            TradeType.CashDividend => L("Portfolio.TradeType.CashDividend", "現金股利"),
            TradeType.StockDividend => L("Portfolio.TradeType.StockDividend", "股票股利"),
            TradeType.Deposit => L("Portfolio.TradeType.Deposit", "存入"),
            TradeType.Withdrawal => L("Portfolio.TradeType.Withdrawal", "提款"),
            TradeType.Transfer => L("Portfolio.TradeType.Transfer", "轉帳"),
            TradeType.LoanBorrow => L("Portfolio.TradeType.LoanBorrow", "借款"),
            TradeType.LoanRepay => L("Portfolio.TradeType.LoanRepay", "還款"),
            TradeType.CreditCardCharge => L("Portfolio.TradeType.CreditCardCharge", "信用卡消費"),
            TradeType.CreditCardPayment => L("Portfolio.TradeType.CreditCardPayment", "信用卡繳款"),
            _ => row.Type.ToString()
        };

        var target = !string.IsNullOrWhiteSpace(row.Symbol)
            ? string.IsNullOrWhiteSpace(row.Name) ? row.Symbol : $"{row.Symbol} {row.Name}"
            : !string.IsNullOrWhiteSpace(row.Name) ? row.Name
            : row.Type switch
            {
                TradeType.Transfer => $"{L("Portfolio.Tx.TransferSourceAccount", "來源帳戶")} / {L("Portfolio.Tx.TransferTargetAccount", "目標帳戶")}",
                _ => "—"
            };

        EditSummaryTarget = target;
        EditSummaryAmount = row.CashAmount is { } cash && cash != 0
            ? cash.ToString("N2")
            : row.Price is { } price && price != 0
                ? price.ToString("N4")
                : "—";
        EditSummaryQuantity = row.Quantity is { } qty && qty != 0
            ? qty.ToString("N4")
            : "—";
    }

    /// <summary>
    /// Called by the parent VM's ApplyLiabilities to refresh the ComboBox binding
    /// for the loan label autocomplete, since LoanLabelSuggestions derives from Liabilities.
    /// </summary>
    internal void NotifyLoanLabelSuggestionsChanged() =>
        OnPropertyChanged(nameof(LoanLabelSuggestions));
}
