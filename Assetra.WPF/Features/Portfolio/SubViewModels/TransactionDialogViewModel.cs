using System.Collections.ObjectModel;
using System.Reactive.Linq;
using Assetra.Application.Portfolio.Contracts;
using Assetra.Application.Portfolio.Dtos;
using Assetra.Core.DomainServices;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Assetra.Core.Trading;
using Assetra.WPF.Features.Categories;
using Assetra.WPF.Features.Portfolio.SubViewModels.Tx;
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
    // Shared parent collections (live references, owned + mutated by parent).
    // Type stays ObservableCollection for cascading consumers (Account/SellPanel/
    // TradeFilter etc. constructor signatures expect by-reference syncing).
    // M6-B mirror full encapsulation tracked separately.
    ObservableCollection<TradeRowViewModel> Trades,
    ObservableCollection<PortfolioRowViewModel> Positions,
    ObservableCollection<CashAccountRowViewModel> CashAccounts,
    ObservableCollection<LiabilityRowViewModel> Liabilities,
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
    Func<Task>? ReloadAllAsync = null);

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
    private IReadOnlyList<AutoCategorizationRule> _autoRulesCache = Array.Empty<AutoCategorizationRule>();

    // Shared parent collections exposed as forwarding properties so TxForm XAML
    // (whose DataContext becomes this VM) can still bind to CashAccounts, Positions, etc.
    public ObservableCollection<TradeRowViewModel> Trades { get; }
    public ObservableCollection<PortfolioRowViewModel> Positions { get; }
    public ObservableCollection<CashAccountRowViewModel> CashAccounts { get; }
    public ObservableCollection<LiabilityRowViewModel> Liabilities { get; }

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
                NotifyImpactPreviewChanged();
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

    /// <summary>Non-null when editing an existing trade (vs. creating new).</summary>
    [ObservableProperty] private Guid? _editingTradeId;
    [ObservableProperty] private bool _isRevisionMode;
    [ObservableProperty] private bool _isRevisionReplacePromptOpen;
    [ObservableProperty] private string _revisionReplacePromptError = string.Empty;
    [ObservableProperty] private bool _isDeleteConfirmOpen;
    [ObservableProperty] private string _deleteTargetName = string.Empty;
    private Guid? _revisionSourceTradeId;
    private bool _preserveRevisionSourceOnClose;

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
    /// <item>Editing a non-meta-only type (Income / CashDividend / Buy with PortfolioEntryId / …)
    /// where the underlying ConfirmTx flow safely supports delete-old + create-new.</item>
    /// </list>
    /// Locks down to false only for meta-only types (Sell / Transfer / legacy Buy without entry),
    /// where the trade has dependent state that direct editing would corrupt.
    /// </summary>
    public bool AreEconomicFieldsEditable => !IsEditMode;

    /// <summary>
    /// True when the dialog should show the locked-core summary card. Only meta-only edits
    /// need this — direct-edit flows show their normal form fields with live values pre-filled.
    /// </summary>
    public bool ShowEditLockedSummary => IsEditMode;

    partial void OnEditingTradeIdChanged(Guid? _)
    {
        OnPropertyChanged(nameof(IsEditMode));
        OnPropertyChanged(nameof(IsEditingMetaOnly));
        OnPropertyChanged(nameof(AreEconomicFieldsEditable));
        OnPropertyChanged(nameof(ShowEditLockedSummary));
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

    // 手續費折扣（每筆交易可調，預設 1.0 = 無折扣）
    [ObservableProperty] private string _txCommissionDiscount = "1.0";

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
                UpdateSellTxPreview();
                break;
            case nameof(SellTxViewModel.Quantity):
                Sell.QuantityError = ValidatePositiveIntOrEmpty(Sell.Quantity);
                UpdateSellTxPreview();
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

    private readonly ObservableCollection<string> _cashAccountSuggestions = new();
    public ReadOnlyObservableCollection<string> CashAccountSuggestions { get; }

    /// <summary>Used by PortfolioViewModel.ApplyCashAccounts to refresh the suggestion list.</summary>
    internal void ReplaceCashAccountSuggestions(IEnumerable<string> names)
    {
        _cashAccountSuggestions.Clear();
        foreach (var n in names) _cashAccountSuggestions.Add(n);
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
        foreach (var s in suggestions) _positionSuggestions.Add(s);
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
            TxCashAccount = null;
        else if (TxCashAccount is null)
            // 優先使用使用者設定的預設帳戶，其次才是列表第一筆
            TxCashAccount = _getDefaultCashAccount()
                            ?? (CashAccounts.Count > 0 ? CashAccounts[0] : null);
    }

    partial void OnTxTypeChanged(string value)
    {
        OnPropertyChanged(nameof(TxTypeIsIncome));
        OnPropertyChanged(nameof(TxTypeIsCashDiv));
        OnPropertyChanged(nameof(TxTypeIsStockDiv));
        OnPropertyChanged(nameof(TxTypeIsCashFlow));
        OnPropertyChanged(nameof(TxTypeIsLoan));
        OnPropertyChanged(nameof(TxTypeIsLoanBorrow));
        OnPropertyChanged(nameof(TxTypeIsLoanRepay));
        OnPropertyChanged(nameof(TxTypeIsCreditCard));
        OnPropertyChanged(nameof(TxTypeIsCreditCardCharge));
        OnPropertyChanged(nameof(TxTypeIsCreditCardPayment));
        OnPropertyChanged(nameof(TxTypeIsTransfer));
        OnPropertyChanged(nameof(TxTypeIsBuy));
        OnPropertyChanged(nameof(TxTypeIsSell));
        // Buy.IsStock/IsNonStock/IsCrypto no longer gate on TxType — XAML form
        // visibility is gated by TxTypeIsBuy on the parent StackPanel, which
        // is already raised above. No notification needed for Buy.X here.
        OnPropertyChanged(nameof(TxCashAccountLabel));
        OnPropertyChanged(nameof(TxUseCashAccountLabel));
        OnPropertyChanged(nameof(TxCashFlowHint));
        OnPropertyChanged(nameof(TxTransferHint));
        OnPropertyChanged(nameof(TxCreditCardHint));
        OnPropertyChanged(nameof(CashFlowCategories));
        TxError = string.Empty;
        ApplyAutoCategoryFromNote();
        UpdateSellTxPreview();
        NotifyImpactPreviewChanged();
        if (TxTypeIsLoanRepay)
            _ = AutoFillLoanRepayAsync(Loan.Label);
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
    /// React to Buy.X changes — runs side effects that the old <c>partial OnTxBuy*Changed</c>
    /// handlers performed (UpdateBuyPreview, write-back AddPrice in total mode, validation).
    /// XAML now binds directly to Buy.X so no facade re-notification needed.
    /// </summary>
    private void OnBuyPriceModeChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(BuyTxViewModel.PriceMode):
                OnPropertyChanged(nameof(TxBuyComputedTotalDisplay));
                break;
            case nameof(BuyTxViewModel.TotalCost):
                if (Buy.IsTotalMode &&
                    ParseHelpers.TryParseDecimal(Buy.TotalCost, out var total) && total > 0 &&
                    ParseHelpers.TryParseInt(AddAssetDialog.AddQuantity, out var qty) && qty > 0)
                {
                    AddAssetDialog.AddPrice = (total / qty).ToString("F4");
                }
                Buy.TotalCostError = ValidatePositiveDecimalOrEmpty(Buy.TotalCost);
                OnPropertyChanged(nameof(TxBuyComputedTotalDisplay));
                break;
            case nameof(BuyTxViewModel.TotalIncludesFee):
                AddAssetDialog.UpdateBuyPreview();
                OnPropertyChanged(nameof(TxBuyComputedTotalDisplay));
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

    internal void OpenTxDialog()
    {
        IsRevisionMode = false;
        var state = _tradeDialogController.CreateOpenState(_getDefaultCashAccount());
        EditingTradeId = null;
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
        TxCommissionDiscountError = string.Empty;
        TxAmount = string.Empty;
        TxNote = string.Empty;
        _suppressCategoryAutoTracking = true;
        try { TxCategoryId = null; } finally { _suppressCategoryAutoTracking = false; }
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
        TxCashAccountName = string.Empty;
        Transfer.Target = null;
        Transfer.TargetName = string.Empty;
        Transfer.TargetAmount = string.Empty;
        CreditCard.Card = null;
        Buy.AssetType = state.TxBuyAssetType;
        Buy.PriceMode = state.TxBuyPriceMode;
        Buy.TotalCost = string.Empty;
        Buy.TotalIncludesFee = true;  // Reset to default — most broker totals include fee.
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
        IsTxDialogOpen = true;
    }

    [RelayCommand]
    private void EditTrade(TradeRowViewModel row)
    {
        IsRevisionMode = false;
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
        TxCommissionDiscount = "1.0";
        Buy.Reset();
        Div.InputMode = "perShare";
        Div.TotalInput = string.Empty;

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
                RestoreCommissionFields(row);
                UpdateSellTxPreview();
                break;

            case TradeType.CashDividend:
                Div.Position = editState.TxDivPosition;
                Div.PerShare = editState.TxDivPerShare;
                TxCashAccount = editState.TxCashAccount;
                TxUseCashAccount = editState.TxUseCashAccount;
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
                break;
        }

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
        // 兩者皆 null（legacy 交易）：保持預設值
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
