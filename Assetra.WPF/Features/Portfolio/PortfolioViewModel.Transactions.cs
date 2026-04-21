using System.Collections.ObjectModel;
using System.Reactive.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using Assetra.Core.Models;
using Assetra.Core.Services;
using Assetra.Core.Trading;
using Assetra.WPF.Infrastructure;

namespace Assetra.WPF.Features.Portfolio;

/// <summary>
/// Transaction dialog state, commands, and all Confirm*Async methods.
/// </summary>
public partial class PortfolioViewModel
{
    // 新增交易 Dialog
    [ObservableProperty] private bool _isTxDialogOpen;
    [ObservableProperty] private string _txType = "income";

    /// <summary>Non-null when editing an existing trade (vs. creating new).</summary>
    [ObservableProperty] private Guid? _editingTradeId;

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

    /// <summary>Inverse of <see cref="IsEditingMetaOnly"/>; bindable from XAML without a converter.</summary>
    public bool AreEconomicFieldsEditable => !IsEditingMetaOnly;

    partial void OnEditingTradeIdChanged(Guid? _)
    {
        OnPropertyChanged(nameof(IsEditMode));
        OnPropertyChanged(nameof(IsEditingMetaOnly));
        OnPropertyChanged(nameof(AreEconomicFieldsEditable));
        DeleteTradeCommand.NotifyCanExecuteChanged();
    }

    // The Tx dialog's shared date picker is bound to TxDate, but ConfirmBuyAsync →
    // AddPosition reads AddBuyDate when building the PortfolioEntry. Without this
    // sync, editing a Buy trade's date changes the picker's TxDate but AddPosition
    // still sees the stale AddBuyDate → the saved buy keeps the old date.
    partial void OnTxDateChanged(DateTime value) => AddBuyDate = value;
    [ObservableProperty] private DateTime _txDate = DateTime.Today;
    [ObservableProperty] private string _txError = string.Empty;

    // ── 欄位驗證訊息（空字串 = 無錯誤；綁定到 XAML FormFieldError TextBlock）──
    [ObservableProperty] private string _txAmountError = string.Empty;
    [ObservableProperty] private string _txFeeError = string.Empty;
    [ObservableProperty] private string _txDivPerShareError = string.Empty;
    [ObservableProperty] private string _txDivTotalInputError = string.Empty;
    [ObservableProperty] private string _txStockDivNewSharesError = string.Empty;
    [ObservableProperty] private string _txTransferTargetAmountError = string.Empty;
    [ObservableProperty] private string _txPrincipalError = string.Empty;
    [ObservableProperty] private string _txInterestPaidError = string.Empty;
    [ObservableProperty] private string _txBuyTotalCostError = string.Empty;
    [ObservableProperty] private string _txCommissionDiscountError = string.Empty;

    private static string ValidatePositiveDecimalOrEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty :
        !ParseHelpers.TryParseDecimal(value, out var v) || v <= 0 ? "請輸入大於 0 的數字" : string.Empty;

    private static string ValidateNonNegativeDecimalOrEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty :
        !ParseHelpers.TryParseDecimal(value, out var v) || v < 0 ? "請輸入 0 或以上的數字" : string.Empty;

    private static string ValidatePositiveIntOrEmpty(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
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

    public static IReadOnlyList<string> IncomeNotes { get; } =
        ["薪資", "獎金", "分紅", "利息收入", "租金收入", "其他"];

    // 現金帳戶選擇（收入 + 現金股利共用，null = 不連動）
    [ObservableProperty] private CashAccountRowViewModel? _txCashAccount;

    // 現金股利欄位
    [ObservableProperty] private PortfolioRowViewModel? _txDivPosition;  // from Positions
    [ObservableProperty] private string _txDivPerShare = string.Empty;
    [ObservableProperty] private decimal _txDivTotal;

    // 股票股利欄位
    [ObservableProperty] private PortfolioRowViewModel? _txStockDivPosition;
    [ObservableProperty] private string _txStockDivNewShares = string.Empty;

    // 手續費折扣（每筆交易可調，預設 1.0 = 無折扣）
    [ObservableProperty] private string _txCommissionDiscount = "1.0";

    /// <summary>解析後的折扣值，用於計算。</summary>
    public decimal TxCommissionDiscountValue =>
        ParseHelpers.TryParseDecimal(TxCommissionDiscount, out var v) && v is > 0 and <= 1 ? v : 1m;

    partial void OnTxCommissionDiscountChanged(string value)
    {
        TxCommissionDiscountError = ValidateCommissionDiscountOrEmpty(value);
        UpdateBuyPreview();
        UpdateSellTxPreview();
    }

    // 買入欄位
    [ObservableProperty] private string _txBuyAssetType = "stock";

    // 賣出欄位
    [ObservableProperty] private PortfolioRowViewModel? _txSellPosition;
    [ObservableProperty] private string _txSellQuantity = string.Empty;
    [ObservableProperty] private string _txSellQuantityError = string.Empty;
    private int _sellQtyOverride;

    // Sell-tx preview — parallel to buy's AddGrossAmount / AddCommission / AddTotalCost,
    // but also shows TransactionTax and NetAmount since sell has two deductions.
    [ObservableProperty] private decimal _txSellGrossAmount;
    [ObservableProperty] private decimal _txSellCommission;
    [ObservableProperty] private decimal _txSellTransactionTax;
    [ObservableProperty] private decimal _txSellNetAmount;
    [ObservableProperty] private bool _txSellIsEtf;
    [ObservableProperty] private bool _txSellIsBondEtf;

    public bool HasTxSellPreview => TxSellGrossAmount > 0;
    partial void OnTxSellGrossAmountChanged(decimal _) => OnPropertyChanged(nameof(HasTxSellPreview));

    partial void OnTxSellQuantityChanged(string value)
    {
        TxSellQuantityError = ValidatePositiveIntOrEmpty(value);
        UpdateSellTxPreview();
    }

    partial void OnTxSellPositionChanged(PortfolioRowViewModel? value)
    {
        if (value is not null && value.CurrentPrice > 0)
            TxAmount = value.CurrentPrice.ToString("F2");
        if (value is null) TxSellQuantity = string.Empty;
        TxSellIsEtf = value is not null && _search.IsEtf(value.Symbol);
        TxSellIsBondEtf = value is not null && _search.IsBondEtf(value.Symbol);
        UpdateSellTxPreview();
    }

    /// <summary>
    /// Recomputes the sell-tx preview (手續費 + 證交稅 + 實得淨額) whenever the sell
    /// price, discount, selected position, or manual fee override changes.
    /// Mirrors <c>UpdateBuyPreview</c> but for the sell side.
    /// </summary>
    private void UpdateSellTxPreview()
    {
        if (!TxTypeIsSell ||
            TxSellPosition is null ||
            !ParseHelpers.TryParseDecimal(TxAmount, out var price) || price <= 0)
        {
            TxSellGrossAmount = 0;
            TxSellCommission = 0;
            TxSellTransactionTax = 0;
            TxSellNetAmount = 0;
            return;
        }

        var qty = ParseHelpers.TryParseInt(TxSellQuantity, out var parsedQty) && parsedQty > 0
            ? parsedQty
            : (int)TxSellPosition.Quantity;
        var gross = price * qty;

        // Manual fee override: user has typed a value in the 手續費 field → treat that as
        // commission + tax combined (same convention as ConfirmSell). Leave the individual
        // breakdown at 0 so the preview card doesn't lie about the split.
        if (!string.IsNullOrWhiteSpace(TxFee) &&
            ParseHelpers.TryParseDecimal(TxFee, out var manualFee) && manualFee >= 0)
        {
            TxSellGrossAmount = gross;
            TxSellCommission = manualFee;
            TxSellTransactionTax = 0;
            TxSellNetAmount = gross - manualFee;
            return;
        }

        var discount = TxCommissionDiscountValue;
        var fee = TaiwanTradeFeeCalculator.CalcSell(price, qty, discount, TxSellIsEtf, TxSellIsBondEtf);
        TxSellGrossAmount = fee.GrossAmount;
        TxSellCommission = fee.Commission;
        TxSellTransactionTax = fee.TransactionTax;
        TxSellNetAmount = fee.NetAmount;
    }

    // Type booleans
    public bool TxTypeIsIncome => TxType == "income";
    public bool TxTypeIsCashDiv => TxType == "cashDiv";
    public bool TxTypeIsStockDiv => TxType == "stockDiv";
    public bool TxTypeIsCashFlow => TxType is "deposit" or "withdrawal";
    public bool TxTypeIsLoan       => TxType is "loanBorrow" or "loanRepay";
    public bool TxTypeIsLoanBorrow => TxType == "loanBorrow";
    public bool TxTypeIsLoanRepay  => TxType == "loanRepay";
    /// <summary>True for 轉帳 — money moves between two cash accounts.</summary>
    public bool TxTypeIsTransfer => TxType == "transfer";
    public bool TxTypeIsBuy => TxType == "buy";
    public bool TxTypeIsSell => TxType == "sell";

    // Buy sub-type predicates
    public bool TxBuyIsStock => TxTypeIsBuy && TxBuyAssetType == "stock";
    public bool TxBuyIsNonStock => TxTypeIsBuy && TxBuyAssetType is "fund" or "metal" or "bond";
    public bool TxBuyIsCrypto => TxTypeIsBuy && TxBuyAssetType == "crypto";

    [ObservableProperty] private string _txLoanLabel = string.Empty;

    partial void OnTxLoanLabelChanged(string value)
    {
        if (TxTypeIsLoanRepay)
            _ = AutoFillLoanRepayAsync(value);
    }

    /// <summary>
    /// Suggestions for the editable loan-label ComboBox — derived from already-recorded loans.
    /// New labels can be typed freely; existing labels appear as dropdown options.
    /// </summary>
    public IReadOnlyList<string> LoanLabelSuggestions =>
        Liabilities.Select(l => l.Label).OrderBy(l => l).ToList();

    // 轉帳 (Transfer)
    // 轉帳會建一組 Withdrawal (源) + Deposit (目) 對：
    //   - 源帳戶用既有的 TxCashAccount property + TxAmount (轉出金額)
    //   - 目帳戶 + 轉入金額用以下兩個新 property（轉入金額可不同 → 跨幣別轉帳）

    /// <summary>轉帳的目標現金帳戶（轉入方）。源是 TxCashAccount。</summary>
    [ObservableProperty] private CashAccountRowViewModel? _txTransferTarget;

    /// <summary>
    /// 轉入金額（目標帳戶收到的金額）。和 TxAmount（源扣款金額）相同 → 同幣別；
    /// 不同 → 跨幣別，可由前端顯示 implied rate = TxAmount / TxTransferTargetAmount。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TxTransferImpliedRateDisplay))]
    private string _txTransferTargetAmount = string.Empty;

    /// <summary>
    /// 隱含匯率顯示文字（給 dialog hint 用）。空白或無效時回 "—"。
    /// </summary>
    public string TxTransferImpliedRateDisplay
    {
        get
        {
            if (!ParseHelpers.TryParseDecimal(TxAmount, out var src) || src <= 0)
                return "—";
            if (!ParseHelpers.TryParseDecimal(TxTransferTargetAmount, out var dst) || dst <= 0)
                return "—";
            var rate = src / dst;
            return rate.ToString("F4");
        }
    }

    partial void OnTxAmountChanged(string value)
    {
        TxAmountError = ValidatePositiveDecimalOrEmpty(value);
        OnPropertyChanged(nameof(TxTransferImpliedRateDisplay));
        UpdateSellTxPreview();
    }

    partial void OnTxFeeChanged(string value)
    {
        TxFeeError = ValidateNonNegativeDecimalOrEmpty(value);
        UpdateBuyPreview();
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
    // the Confirm commands call IAssetRepository.FindOrCreateAccountAsync on submit.
    // The ComboBox's SelectedItem (TxCashAccount) remains authoritative when set;
    // TxCashAccountName only wins when TxCashAccount is null AND the name is non-empty.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNewCashAccount))]
    private string _txCashAccountName = string.Empty;

    public bool IsNewCashAccount =>
        !string.IsNullOrWhiteSpace(TxCashAccountName)
        && !CashAccountSuggestions.Any(s =>
            string.Equals(s, TxCashAccountName, StringComparison.OrdinalIgnoreCase));

    public ObservableCollection<string> CashAccountSuggestions { get; } = new();

    // ── Transfer destination — same coexistence pattern as TxCashAccountName ──────────
    // INVARIANT: TxTransferTarget (SelectedItem picker) wins when set.
    // TxTransferTargetName (typed text) only wins when TxTransferTarget is null
    // AND the name is non-empty. IsNewTransferTarget drives the "will create new" hint.
    // Task 19.
    /// <summary>
    /// Transfer destination — typed text coexisting with TxTransferTarget (the picker).
    /// When the text doesn't match any existing account, IsNewTransferTarget returns true
    /// and ConfirmTransferAsync routes through FindOrCreateAccountAsync on the destination side.
    /// Task 19.
    /// </summary>
    [NotifyPropertyChangedFor(nameof(IsNewTransferTarget))]
    [ObservableProperty] private string _txTransferTargetName = string.Empty;

    // INVARIANT: picker-first-then-text — same contract as IsNewCashAccount.
    public bool IsNewTransferTarget =>
        !string.IsNullOrWhiteSpace(TxTransferTargetName)
        && !CashAccountSuggestions.Any(s =>
            string.Equals(s, TxTransferTargetName, StringComparison.OrdinalIgnoreCase));

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

    public ObservableCollection<PositionSuggestion> PositionSuggestions { get; } = new();

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

        if (!string.IsNullOrWhiteSpace(TxCashAccountName) && _assetRepo is not null)
        {
            // Currency defaults to TWD — matches AddAccountAsync default. Task 19 may
            // thread an explicit currency when the TX form adds a currency picker.
            var id = await _assetRepo
                .FindOrCreateAccountAsync(TxCashAccountName.Trim(), "TWD")
                .ConfigureAwait(true);
            return id;
        }

        return requireAccount ? (Guid?)null : null; // caller decides whether null is fatal
    }

    /// <summary>
    /// Destination-side analogue of <see cref="ResolveCashAccountIdAsync"/> for
    /// Transfer's target account. Same two-tier resolution:
    ///   1. TxTransferTarget (ComboBox SelectedItem) — authoritative when set.
    ///   2. TxTransferTargetName (typed text) → FindOrCreateAccountAsync. TWD default currency.
    /// Task 19.
    /// </summary>
    // INVARIANT: picker-first-then-text — TxTransferTarget.Id wins when the picker has a
    // selection; only falls through to FindOrCreateAccountAsync when TxTransferTarget is null.
    private async Task<Guid?> ResolveTransferTargetIdAsync()
    {
        if (TxTransferTarget is not null)
            return TxTransferTarget.Id;

        if (!string.IsNullOrWhiteSpace(TxTransferTargetName) && _assetRepo is not null)
        {
            return await _assetRepo
                .FindOrCreateAccountAsync(TxTransferTargetName.Trim(), "TWD")
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
            TxCashAccount = GetDefaultCashAccount()
                            ?? (CashAccounts.Count > 0 ? CashAccounts[0] : null);
    }

    partial void OnTxTypeChanged(string _)
    {
        OnPropertyChanged(nameof(TxTypeIsIncome));
        OnPropertyChanged(nameof(TxTypeIsCashDiv));
        OnPropertyChanged(nameof(TxTypeIsStockDiv));
        OnPropertyChanged(nameof(TxTypeIsCashFlow));
        OnPropertyChanged(nameof(TxTypeIsLoan));
        OnPropertyChanged(nameof(TxTypeIsLoanBorrow));
        OnPropertyChanged(nameof(TxTypeIsLoanRepay));
        OnPropertyChanged(nameof(TxTypeIsTransfer));
        OnPropertyChanged(nameof(TxTypeIsBuy));
        OnPropertyChanged(nameof(TxTypeIsSell));
        OnPropertyChanged(nameof(TxBuyIsStock));
        OnPropertyChanged(nameof(TxBuyIsNonStock));
        OnPropertyChanged(nameof(TxBuyIsCrypto));
        TxError = string.Empty;
        UpdateSellTxPreview();
        if (TxTypeIsLoanRepay)
#pragma warning disable CS4014
            AutoFillLoanRepayAsync(TxLoanLabel);
#pragma warning restore CS4014
    }

    partial void OnTxBuyAssetTypeChanged(string _)
    {
        OnPropertyChanged(nameof(TxBuyIsStock));
        OnPropertyChanged(nameof(TxBuyIsNonStock));
        OnPropertyChanged(nameof(TxBuyIsCrypto));
    }

    partial void OnTxDivPositionChanged(PortfolioRowViewModel? _) => UpdateDivTotal();

    partial void OnTxDivPerShareChanged(string value)
    {
        TxDivPerShareError = ValidatePositiveDecimalOrEmpty(value);
        UpdateDivTotal();
    }

    private void UpdateDivTotal()
    {
        if (TxDivPosition is null || !ParseHelpers.TryParseDecimal(TxDivPerShare, out var perShare) || perShare <= 0)
            TxDivTotal = 0;
        else
            TxDivTotal = perShare * TxDivPosition.Quantity;
    }

    public bool HasDivPreview => TxDivTotal > 0;
    partial void OnTxDivTotalChanged(decimal _) => OnPropertyChanged(nameof(HasDivPreview));

    // 賣出現金帳戶連動 — bridge for the dedicated SellPanel.xaml; in TX dialog Sell flow
    // we use TxCashAccount and copy across in ConfirmSellTxAsync.
    [ObservableProperty] private CashAccountRowViewModel? _sellCashAccount;

    // Buy 價格輸入模式 + 總額
    /// <summary>
    /// 買入價格輸入模式：<c>"unit"</c> = 填單價（系統算總額）；<c>"total"</c> = 填總額（系統算單價）。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TxBuyIsUnitMode))]
    [NotifyPropertyChangedFor(nameof(TxBuyIsTotalMode))]
    private string _txBuyPriceMode = "unit";

    /// <summary>
    /// When true (and TxBuyAssetType == "stock"), ConfirmBuy writes only the
    /// PortfolioEntry (via FindOrCreatePortfolioEntryAsync) and skips the Buy
    /// trade row — produces a watchlist-style Qty=0 position. The UI hides the
    /// price/quantity/commission/cash-account inputs in this mode.
    /// Plan Task 18 (Option B).
    /// </summary>
    [ObservableProperty] private bool _txBuyMetaOnly;

    public bool TxBuyIsUnitMode => TxBuyPriceMode == "unit";
    public bool TxBuyIsTotalMode => TxBuyPriceMode == "total";

    /// <summary>「總額」模式下使用者輸入的總成交金額（不含手續費）。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TxBuyComputedTotalDisplay))]
    private string _txBuyTotalCost = string.Empty;

    /// <summary>
    /// 總計顯示文字：<c>「總計：NT$X」</c>。
    /// 單價模式 → 顯示 數量 × 單價。
    /// 總額模式 → 顯示使用者輸入的總額（保持一致）。
    /// </summary>
    public string TxBuyComputedTotalDisplay
    {
        get
        {
            if (TxBuyIsTotalMode &&
                ParseHelpers.TryParseDecimal(TxBuyTotalCost, out var t) && t > 0)
                return t.ToString("N0");
            if (ParseHelpers.TryParseDecimal(AddPrice, out var p) && p > 0 &&
                ParseHelpers.TryParseInt(AddQuantity, out var q) && q > 0)
                return (p * q).ToString("N0");
            return "0";
        }
    }

    partial void OnTxBuyTotalCostChanged(string value)
    {
        TxBuyTotalCostError = ValidatePositiveDecimalOrEmpty(value);
        // 總額模式時，回算單價以維持持倉成本計算邏輯。
        if (TxBuyIsTotalMode &&
            ParseHelpers.TryParseDecimal(value, out var total) && total > 0 &&
            ParseHelpers.TryParseInt(AddQuantity, out var qty) && qty > 0)
        {
            AddPrice = (total / qty).ToString("F4");
        }
    }

    // CashDividend 輸入模式
    /// <summary>
    /// 股息輸入模式：<c>"perShare"</c> = 填每股股利；<c>"total"</c> = 直接填總股息金額。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TxDivIsPerShareMode))]
    [NotifyPropertyChangedFor(nameof(TxDivIsTotalMode))]
    private string _txDivInputMode = "perShare";

    public bool TxDivIsPerShareMode => TxDivInputMode == "perShare";
    public bool TxDivIsTotalMode => TxDivInputMode == "total";

    /// <summary>總額模式下使用者直接輸入的總股息。</summary>
    [ObservableProperty] private string _txDivTotalInput = string.Empty;

    partial void OnTxDivTotalInputChanged(string value) =>
        TxDivTotalInputError = ValidatePositiveDecimalOrEmpty(value);

    partial void OnTxStockDivNewSharesChanged(string value) =>
        TxStockDivNewSharesError = ValidatePositiveIntOrEmpty(value);

    partial void OnTxTransferTargetAmountChanged(string value) =>
        TxTransferTargetAmountError = ValidatePositiveDecimalOrEmpty(value);

    // LoanRepay 拆分欄位
    [ObservableProperty] private string _txPrincipal = string.Empty;
    [ObservableProperty] private string _txInterestPaid = string.Empty;

    partial void OnTxPrincipalChanged(string value) =>
        TxPrincipalError = ValidatePositiveDecimalOrEmpty(value);

    partial void OnTxInterestPaidChanged(string value) =>
        TxInterestPaidError = ValidateNonNegativeDecimalOrEmpty(value);

    // LoanBorrow 攤還欄位（選填；填寫後自動建立攤還表）
    [ObservableProperty] private string   _txLoanRate       = string.Empty;
    [ObservableProperty] private string   _txLoanTermMonths = string.Empty;
    [ObservableProperty] private DateTime _txLoanStartDate  = DateTime.Today;
    [ObservableProperty] private string   _txLoanRateError       = string.Empty;
    [ObservableProperty] private string   _txLoanTermMonthsError = string.Empty;

    partial void OnTxLoanRateChanged(string value) =>
        TxLoanRateError = ValidateNonNegativeDecimalOrEmpty(value);

    partial void OnTxLoanTermMonthsChanged(string value) =>
        TxLoanTermMonthsError = string.IsNullOrWhiteSpace(value) ? string.Empty
            : (ParseHelpers.TryParseInt(value, out var n) && n > 0 ? string.Empty : "請輸入正整數");

    // 新增交易 Dialog commands

    private void OpenTxDialog()
    {
        EditingTradeId = null;
        TxType = "buy";
        TxDate = DateTime.Today;
        TxError = string.Empty;
        TxAmountError = string.Empty;
        TxFeeError = string.Empty;
        TxDivPerShareError = string.Empty;
        TxDivTotalInputError = string.Empty;
        TxStockDivNewSharesError = string.Empty;
        TxTransferTargetAmountError = string.Empty;
        TxPrincipalError = string.Empty;
        TxInterestPaidError = string.Empty;
        TxBuyTotalCostError = string.Empty;
        TxCommissionDiscountError = string.Empty;
        TxAmount = string.Empty;
        TxNote = string.Empty;
        // 新增（非編輯）時帶入使用者在現金頁設定的預設帳戶
        TxCashAccount = GetDefaultCashAccount();
        TxDivPosition = null;
        TxDivPerShare = string.Empty;
        TxDivTotal = 0;
        TxStockDivPosition = null;
        TxStockDivNewShares = string.Empty;
        TxLoanLabel = string.Empty;
        TxFee = string.Empty;
        TxUseCashAccount = true;
        TxCashAccountName = string.Empty;
        TxTransferTarget = null;
        TxTransferTargetName = string.Empty;
        TxTransferTargetAmount = string.Empty;
        TxBuyAssetType = "stock";
        TxBuyPriceMode = "unit";
        TxBuyTotalCost = string.Empty;
        TxBuyMetaOnly = false;
        TxDivInputMode = "perShare";
        TxDivTotalInput = string.Empty;
        TxCommissionDiscount = "1.0";
        TxSellPosition = null;
        TxSellQuantity = string.Empty;
        TxSellQuantityError = string.Empty;
        TxPrincipal = string.Empty;
        TxInterestPaid = string.Empty;
        TxLoanRate = string.Empty;
        TxLoanTermMonths = string.Empty;
        TxLoanStartDate = DateTime.Today;
        TxLoanRateError = string.Empty;
        TxLoanTermMonthsError = string.Empty;
        // Buy-specific fields shared with AddAssetDialog
        AddSymbol = string.Empty;
        AddPrice = string.Empty;
        AddQuantity = string.Empty;
        AddBuyDate = DateTime.Today;
        AddError = string.Empty;
        AddName = string.Empty;
        AddCost = string.Empty;
        AddCryptoSymbol = string.Empty;
        AddCryptoQty = string.Empty;
        AddCryptoPrice = string.Empty;
        SellCashAccount = null;
        SellPriceInput = string.Empty;
        AddPriceError = string.Empty;
        AddQuantityError = string.Empty;
        AddCostError = string.Empty;
        AddCryptoQtyError = string.Empty;
        AddCryptoPriceError = string.Empty;
        IsTxDialogOpen = true;
    }

    [RelayCommand]
    private void EditTrade(TradeRowViewModel row)
    {
        EditingTradeId = row.Id;
        TxDate = row.TradeDate.ToLocalTime();
        TxError = string.Empty;
        TxAmountError = string.Empty;
        TxFeeError = string.Empty;
        TxDivPerShareError = string.Empty;
        TxDivTotalInputError = string.Empty;
        TxStockDivNewSharesError = string.Empty;
        TxTransferTargetAmountError = string.Empty;
        TxPrincipalError = string.Empty;
        TxInterestPaidError = string.Empty;
        TxBuyTotalCostError = string.Empty;
        TxCommissionDiscountError = string.Empty;
        TxNote = row.Note ?? string.Empty;

        // Reset every type-specific field first so state from a previous open doesn't bleed
        // through (e.g. editing Income after editing Buy would otherwise leave AddSymbol set).
        TxAmount = string.Empty;
        TxCashAccount = null;
        TxCashAccountName = string.Empty;
        TxLoanLabel = string.Empty;
        TxFee = string.Empty;
        TxUseCashAccount = true;
        TxTransferTarget = null;
        TxTransferTargetName = string.Empty;
        TxTransferTargetAmount = string.Empty;
        TxPrincipal = string.Empty;
        TxInterestPaid = string.Empty;
        TxDivPosition = null;
        TxDivPerShare = string.Empty;
        TxStockDivPosition = null;
        TxStockDivNewShares = string.Empty;
        TxSellPosition = null;
        TxSellQuantity = string.Empty;
        TxSellQuantityError = string.Empty;
        SellCashAccount = null;
        SellPriceInput = string.Empty;
        AddSymbol = string.Empty;
        AddPrice = string.Empty;
        AddQuantity = string.Empty;
        AddPriceError = string.Empty;
        AddQuantityError = string.Empty;
        AddCostError = string.Empty;
        AddCryptoQtyError = string.Empty;
        AddCryptoPriceError = string.Empty;
        TxCommissionDiscount = "1.0";
        TxBuyAssetType = "stock";
        TxBuyPriceMode = "unit";
        TxBuyTotalCost = string.Empty;
        TxBuyMetaOnly = false;
        TxDivInputMode = "perShare";
        TxDivTotalInput = string.Empty;

        // Map TradeType → TxType string (Buy / Sell now explicitly handled)
        TxType = row.Type switch
        {
            TradeType.Buy => "buy",
            TradeType.Sell => "sell",
            TradeType.Income => "income",
            TradeType.CashDividend => "cashDiv",
            TradeType.StockDividend => "stockDiv",
            TradeType.Deposit => "deposit",
            TradeType.Withdrawal => "withdrawal",
            TradeType.Transfer => "transfer",
            TradeType.LoanBorrow => "loanBorrow",
            TradeType.LoanRepay => "loanRepay",
            _ => "income",
        };

        switch (row.Type)
        {
            case TradeType.Buy:
                // Full edit path when linked to a lot; meta-only otherwise (dialog locks fields).
                // Suppress the symbol-search suggestions popup during pre-fill — the popup
                // exists for live search-as-you-type, not for showing what the user is
                // already editing.
                _suppressSuggestions = true;
                AddSymbol = row.Symbol;
                _suppressSuggestions = false;
                IsSuggestionsOpen = false;
                AddPrice = row.Price.ToString("F4");
                AddQuantity = row.Quantity.ToString();
                AddBuyDate = TxDate;
                TxCashAccount = row.CashAccountId is { } buyAcc
                    ? CashAccounts.FirstOrDefault(c => c.Id == buyAcc) : null;
                TxUseCashAccount = TxCashAccount is not null;
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
                TxSellPosition = Positions.FirstOrDefault(p => p.Symbol == row.Symbol);
                TxSellQuantity = row.Quantity.ToString();
                TxAmount = row.Price.ToString("F4");
                SellPriceInput = row.Price.ToString("F4");
                var sellAccLookup = row.CashAccountId is { } sellAcc
                    ? CashAccounts.FirstOrDefault(c => c.Id == sellAcc) : null;
                TxCashAccount = sellAccLookup;
                SellCashAccount = sellAccLookup;
                TxUseCashAccount = sellAccLookup is not null;
                RestoreCommissionFields(row);
                break;

            case TradeType.CashDividend:
                TxDivPosition = Positions.FirstOrDefault(p => p.Symbol == row.Symbol);
                TxDivPerShare = row.Price > 0 ? row.Price.ToString("F4") : string.Empty;
                TxCashAccount = row.CashAccountId is { } divAcc
                    ? CashAccounts.FirstOrDefault(c => c.Id == divAcc) : null;
                TxUseCashAccount = TxCashAccount is not null;
                break;

            case TradeType.StockDividend:
                TxStockDivPosition = Positions.FirstOrDefault(p => p.Symbol == row.Symbol);
                TxStockDivNewShares = row.Quantity.ToString();
                break;

            case TradeType.Income:
            case TradeType.Deposit:
            case TradeType.Withdrawal:
                TxAmount = row.CashAmount?.ToString("F0") ?? string.Empty;
                TxCashAccount = row.CashAccountId is { } cashAcc
                    ? CashAccounts.FirstOrDefault(c => c.Id == cashAcc) : null;
                break;

            case TradeType.LoanBorrow:
                TxAmount = row.CashAmount?.ToString("F0") ?? string.Empty;
                TxLoanLabel = row.LoanLabel ?? row.Name;
                TxCashAccount = row.CashAccountId is { } borrowCashAcc
                    ? CashAccounts.FirstOrDefault(c => c.Id == borrowCashAcc) : null;
                TxUseCashAccount = TxCashAccount is not null;
                TxFee = string.Empty;
                break;

            case TradeType.LoanRepay:
                // CashAmount = Principal + InterestPaid（向後相容：Principal null 代表全額為本金）
                TxPrincipal = row.Principal.HasValue
                    ? Math.Round(row.Principal.Value, 0).ToString("F0")
                    : row.CashAmount?.ToString("F0") ?? string.Empty;
                TxInterestPaid = row.InterestPaid.HasValue
                    ? Math.Round(row.InterestPaid.Value, 0).ToString("F0")
                    : "0";
                TxLoanLabel = row.LoanLabel ?? row.Name;
                TxCashAccount = row.CashAccountId is { } repayCashAcc
                    ? CashAccounts.FirstOrDefault(c => c.Id == repayCashAcc) : null;
                TxUseCashAccount = TxCashAccount is not null;
                // Fee can't be inferred from trade record alone; leave blank.
                TxFee = string.Empty;
                break;

            case TradeType.Transfer:
                // Meta-only edit: show transfer values for reference; only date/note are editable.
                // IsMetaOnlyEditType is true for Transfer, so ConfirmTx takes the meta-only path.
                TxAmount = row.CashAmount?.ToString("F0") ?? string.Empty;
                TxTransferTargetAmount = row.CashAmount?.ToString("F0") ?? string.Empty;
                TxCashAccount = row.CashAccountId is { } txSrcAcc
                    ? CashAccounts.FirstOrDefault(c => c.Id == txSrcAcc) : null;
                TxTransferTarget = row.ToCashAccountId is { } txDstAcc
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
        EditingTradeId = null;
        IsTxDialogOpen = false;
    }

    /// <summary>
    /// 刪除目前正在編輯的交易。只在 <see cref="IsEditMode"/> 為 true 時可執行。
    /// Buy / Sell / StockDividend 會同步清理關聯的 <see cref="PortfolioEntry"/>。
    /// </summary>
    [RelayCommand(CanExecute = nameof(IsEditMode))]
    private async Task DeleteTradeAsync()
    {
        if (EditingTradeId is not { } id)
            return;

        var row = Trades.FirstOrDefault(t => t.Id == id);
        if (row is null)
            return;

        // Guard: deleting a Buy or StockDividend that is "covered" by a Sell trade would
        // produce a negative net position — block and require the user to delete the Sell first.
        if (await WouldRemovalCauseNegativeQtyAsync(row))
        {
            TxError = L("Portfolio.Trade.DeleteBlockedBySell",
                "請先刪除此股票的賣出記錄，再刪除此買入記錄。");
            return;
        }

        // 同步 stock-lot（Buy/Sell/StockDividend 需要更新或移除 PortfolioEntry）
        await ApplyOldTradeRemovalOnPositionAsync(row);

        try
        {
            // 先刪子記錄（手續費等附屬 Withdrawal），再刪主記錄
            await _tradeRepo.RemoveChildrenAsync(id);
            await _tradeRepo.RemoveAsync(id);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to delete trade {TradeId}", id);
            _snackbar?.Error(L("Portfolio.Trade.OldDeleteFailed", "交易刪除失敗"));
            return;
        }

        CloseTxDialog();
        await LoadPositionsAsync();
        await LoadTradesAsync();
        await ReloadAccountBalancesAsync();
        RebuildTotals();
    }

    [RelayCommand]
    private async Task ConfirmTx()
    {
        TxError = string.Empty;

        // Snapshot the edit target but don't touch it yet. Each Confirm*Async does its own
        // validation and sets TxError on failure; if the new values are rejected we need the
        // old trade intact so the dialog can stay open with the edit context preserved.
        // This replaces the former "delete old before dispatch" pattern which could leave
        // the ledger in a corrupt state when validation failed (old gone, new never created).
        var pendingEditId = EditingTradeId;
        var oldRow = pendingEditId is { } id ? Trades.FirstOrDefault(t => t.Id == id) : null;

        // Clear EditingTradeId so downstream handlers (e.g. inline "which symbol is this
        // buy for" logic) don't mistake this for an in-progress edit loop.
        EditingTradeId = null;

        // Meta-only edit path: Sell trades and Buy trades without a PortfolioEntryId link
        // (legacy data whose lot couldn't be identified). Update the existing row in place
        // with new date/note instead of the full delete+create flow, because:
        //   - Sells can't re-run ConfirmSell (the lot is already gone)
        //   - Unlinked Buys can't safely replace their lot (no way to find which lot)
        // Price/Quantity/Symbol changes are not supported in these cases — the edit dialog
        // locks those fields.
        if (oldRow is not null && IsMetaOnlyEdit(oldRow))
        {
            await UpdateTradeMetaOnlyAsync(oldRow);
            return;
        }

        switch (TxType)
        {
            case "income":
                await ConfirmIncomeAsync();
                break;
            case "cashDiv":
                await ConfirmCashDivAsync();
                break;
            case "stockDiv":
                await ConfirmStockDivAsync();
                break;
            case "deposit":
                await ConfirmCashFlowAsync(TradeType.Deposit);
                break;
            case "withdrawal":
                await ConfirmCashFlowAsync(TradeType.Withdrawal);
                break;
            case "loanBorrow":
                await ConfirmLoanAsync(TradeType.LoanBorrow);
                break;
            case "loanRepay":
                await ConfirmLoanAsync(TradeType.LoanRepay);
                break;
            case "transfer":
                await ConfirmTransferAsync();
                break;
            case "buy":
                await ConfirmBuyAsync();
                break;
            case "sell":
                await ConfirmSellTxAsync();
                break;
        }

        // Validation / creation failed → restore edit mode and leave the old trade alone.
        if (!string.IsNullOrEmpty(TxError))
        {
            EditingTradeId = pendingEditId;
            return;
        }

        // New trade is in the ledger. Under the single-truth model, balances are a pure
        // projection over remaining journal entries — just delete the old row and re-project.
        // Note: oldRow is a snapshot taken before LoadTradesAsync (called inside the
        // Confirm*Async), so it's detached from the current Trades collection but its
        // data fields are still valid for the stock-lot cleanup below.
        if (oldRow is not null)
        {
            // Guard: editing a Buy/StockDividend that covers an existing Sell would leave
            // a negative position if the old (larger) lot is removed. Reject and restore
            // the edit context so the user can fix the issue first.
            if (await WouldRemovalCauseNegativeQtyAsync(oldRow))
            {
                TxError = L("Portfolio.Trade.DeleteBlockedBySell",
                    "請先刪除此股票的賣出記錄，再修改此買入記錄。");
                EditingTradeId = pendingEditId;
                return;
            }

            // Stock-position trades also need the owning lot updated — see
            // ApplyOldTradeRemovalOnPositionAsync for the Buy/Sell/StockDividend logic.
            await ApplyOldTradeRemovalOnPositionAsync(oldRow);
            try
            {
                // 先刪子記錄（手續費等附屬 Withdrawal），再刪主記錄
                await _tradeRepo.RemoveChildrenAsync(oldRow.Id);
                await _tradeRepo.RemoveAsync(oldRow.Id);
            }
            catch (Exception ex)
            {
                // The new trade was already written; failing to delete the old one would
                // leave both records in the ledger and show a duplicate entry to the user.
                Log.Error(ex, "Failed to remove old trade {TradeId} during edit — possible duplicate entry", oldRow.Id);
                _snackbar?.Error(L("Portfolio.Trade.OldDeleteFailed", "舊交易記錄刪除失敗，資料庫可能存在重複筆數，建議重新整理或重啟應用程式"));
            }
            await LoadPositionsAsync();
            await LoadTradesAsync();
            await ReloadAccountBalancesAsync();
            RebuildTotals();
        }
    }

    /// <summary>
    /// Thin wrapper delegating to <see cref="TradeRowViewModel.IsMetaOnlyEditType"/> so
    /// the rule lives in one place (the row VM) and both ConfirmTx and the dialog XAML
    /// (<see cref="IsEditingMetaOnly"/>) agree on what "meta-only" means.
    /// </summary>
    private static bool IsMetaOnlyEdit(TradeRowViewModel row) => row.IsMetaOnlyEditType;

    /// <summary>
    /// In-place UPDATE of an existing trade — used for Sell edits and legacy unlinked
    /// Buy/StockDividend edits. Preserves the trade Id (and therefore <see cref="Trade.PortfolioEntryId"/>
    /// and any downstream references). Only date and note are considered editable; all
    /// economic fields (price, quantity, cash amount) are copied from the original row so
    /// balances don't need to be re-reconciled.
    /// </summary>
    private async Task UpdateTradeMetaOnlyAsync(TradeRowViewModel oldRow)
    {
        try
        {
            // Re-fetch the full Trade record (the row VM doesn't carry every column).
            var all = await _tradeRepo.GetAllAsync();
            var original = all.FirstOrDefault(t => t.Id == oldRow.Id);
            if (original is null)
                return;

            var updated = original with
            {
                TradeDate = DateTime.SpecifyKind(TxDate, DateTimeKind.Local).ToUniversalTime(),
                Note = string.IsNullOrWhiteSpace(TxNote) ? null : TxNote,
            };
            await _tradeRepo.UpdateAsync(updated);
            CloseTxDialog();
            await LoadTradesAsync();
        }
        catch (Exception ex)
        {
            TxError = $"更新失敗：{ex.Message}";
        }
    }

    /// <summary>
    /// When a Buy / Sell / StockDividend trade is removed as part of an edit, update the
    /// linked <see cref="PortfolioEntry"/> accordingly:
    /// <list type="bullet">
    /// <item><description><b>Buy</b>: the new trade's <see cref="AddPosition"/> already created
    /// a fresh entry with the new values. The OLD lot's entry is now stale and gets removed
    /// (or kept if the Buy was for an existing multi-buy lot — harder case, see notes).</description></item>
    /// <item><description><b>Sell</b>: the entry was already removed at sell time; nothing to do
    /// here. Historic Sell edits are meta-only.</description></item>
    /// <item><description><b>StockDividend</b>: subtract the old share count from the entry so
    /// the new StockDividend's add lands on a clean baseline.</description></item>
    /// </list>
    /// </summary>
    /// <summary>
    /// Returns true if removing <paramref name="row"/> from the ledger would cause
    /// the net position quantity to drop below zero — meaning a Sell trade has already
    /// consumed shares this Buy/StockDividend was accounting for.
    /// </summary>
    private async Task<bool> WouldRemovalCauseNegativeQtyAsync(TradeRowViewModel row)
    {
        if (row.Type is not (TradeType.Buy or TradeType.StockDividend))
            return false;
        if (row.PortfolioEntryId is not { } entryId)
            return false;
        var snap = await _positionQuery.GetPositionAsync(entryId);
        return snap is not null && snap.Quantity - (decimal)row.Quantity < 0;
    }

    private async Task ApplyOldTradeRemovalOnPositionAsync(TradeRowViewModel oldRow)
    {
        if (_repo is null)
            return;
        if (oldRow.PortfolioEntryId is not { } entryId)
            return;

        switch (oldRow.Type)
        {
            case TradeType.Buy:
                // Only hard-delete the portfolio entry if this is the last trade referencing it.
                // Stocks share one entry across all buy lots (FindOrCreatePortfolioEntryAsync),
                // so deleting the entry when other buys still reference it would silently
                // destroy those positions after the next app restart.
                // Display is refreshed by the LoadPositionsAsync() call in DeleteTradeAsync.
                var refs = await _repo.HasTradeReferencesAsync(entryId);
                if (refs <= 1)
                    await _repo.RemoveAsync(entryId);
                break;

            case TradeType.StockDividend:
                // No DB entry manipulation needed — trade log is the source of truth.
                // LoadPositionsAsync() recomputes quantity from trades after deletion.
                break;

            case TradeType.Sell:
                // ConfirmSell now archives lots (soft-delete) rather than removing them,
                // so deleting a sell trade should restore all archived lots for this symbol.
                var allEntries = await _repo.GetEntriesAsync();
                foreach (var entry in allEntries)
                {
                    if (string.Equals(entry.Symbol, oldRow.Symbol, StringComparison.OrdinalIgnoreCase)
                        && !entry.IsActive)
                        await _repo.UnarchiveAsync(entry.Id);
                }
                break;
        }
    }

    private async Task ConfirmBuyAsync()
    {
        // Delegate to existing buy logic based on asset sub-type.
        // The sub-methods close IsAddDialogOpen on success; we also close IsTxDialogOpen.

        _ = AddError;

        switch (TxBuyAssetType)
        {
            case "stock":
                await AddPosition();
                break;
            case "fund":
                await AddNonStockAsync(AssetType.Fund);
                break;
            case "metal":
                await AddNonStockAsync(AssetType.PreciousMetal);
                break;
            case "bond":
                await AddNonStockAsync(AssetType.Bond);
                break;
            case "crypto":
                await AddCryptoAsync();
                break;
        }

        // Propagate error to TxError for display in the Tx dialog
        if (!string.IsNullOrEmpty(AddError))
        {
            TxError = AddError;
            return;
        }

        // Close Tx dialog (the sub-methods may have closed AddDialog already)
        IsAddDialogOpen = false;
        IsTxDialogOpen = false;
    }

    private async Task ConfirmSellTxAsync()
    {
        if (TxSellPosition is null)
        { TxError = "請選擇持倉"; return; }

        if (!ParseHelpers.TryParseInt(TxSellQuantity, out var sellQty) || sellQty <= 0)
        { TxError = "賣出數量無效"; return; }
        if (sellQty > (int)TxSellPosition.Quantity)
        { TxError = $"賣出數量 ({sellQty:N0}) 超過持倉 ({(int)TxSellPosition.Quantity:N0}) 股"; return; }

        SellingRow = TxSellPosition;
        IsSellEtf = _search.IsEtf(TxSellPosition.Symbol);

        if (!ParseHelpers.TryParseDecimal(TxAmount, out var sellPrice) || sellPrice <= 0)
        { TxError = "賣出價格無效"; return; }

        SellPriceInput = sellPrice.ToString();
        SellCashAccount = TxUseCashAccount ? TxCashAccount : null;
        _sellQtyOverride = sellQty;

        await ConfirmSell();
        _sellQtyOverride = 0;

        if (!string.IsNullOrEmpty(SellPanelError))
        {
            TxError = SellPanelError;
            return;
        }

        IsTxDialogOpen = false;
    }

    private async Task ConfirmIncomeAsync()
    {
        if (!ParseHelpers.TryParseDecimal(TxAmount, out var amount) || amount <= 0)
        { TxError = "金額無效"; return; }
        var fee = ParseOptionalFee(out var feeError);
        if (feeError is not null)
        { TxError = feeError; return; }

        var cashAccId = await ResolveCashAccountIdAsync();
        var tradeDate = DateTime.SpecifyKind(TxDate, DateTimeKind.Local).ToUniversalTime();
        var trade = new Trade(
            Id: Guid.NewGuid(),
            Symbol: string.Empty,
            Exchange: string.Empty,
            Name: TxNote,
            Type: TradeType.Income,
            TradeDate: tradeDate,
            Price: 0,
            Quantity: 1,
            RealizedPnl: null,
            RealizedPnlPct: null,
            CashAmount: amount,
            CashAccountId: cashAccId,
            Note: TxNote);

        await _txService.RecordAsync(trade);

        await WriteFeeTradeIfAnyAsync(fee, cashAccId, tradeDate,
            $"{TxNote} 手續費", null, trade.Id);

        await ReloadAccountBalancesAsync();
        CloseTxDialog();
        await LoadTradesAsync();
    }

    /// <summary>
    /// 現金股利 → 連動現金帳戶。支援兩種輸入模式：
    /// <list type="bullet">
    /// <item><description><b>perShare</b>：填每股股利 → total = perShare × 持股數</description></item>
    /// <item><description><b>total</b>：直接填總股息金額 → perShare = total / 持股數（用於存進 Trade.Price）</description></item>
    /// </list>
    /// 可選填手續費（如二代健保補充費），會額外建一筆 Withdrawal 連動現金帳戶。
    /// </summary>
    private async Task ConfirmCashDivAsync()
    {
        if (TxDivPosition is null)
        { TxError = "請選擇股票"; return; }

        decimal perShare;
        decimal total;
        if (TxDivIsTotalMode)
        {
            if (!ParseHelpers.TryParseDecimal(TxDivTotalInput, out total) || total <= 0)
            { TxError = "總股息金額無效"; return; }
            perShare = TxDivPosition.Quantity > 0 ? total / TxDivPosition.Quantity : 0;
        }
        else
        {
            if (!ParseHelpers.TryParseDecimal(TxDivPerShare, out perShare) || perShare <= 0)
            { TxError = "每股股利無效"; return; }
            total = perShare * TxDivPosition.Quantity;
        }

        var fee = ParseOptionalFee(out var feeError);
        if (feeError is not null)
        { TxError = feeError; return; }

        var divName = string.IsNullOrEmpty(TxDivPosition.Name)
                      ? TxDivPosition.Symbol
                      : TxDivPosition.Name;
        var tradeDate = DateTime.SpecifyKind(TxDate, DateTimeKind.Local).ToUniversalTime();
        var cashAccId = TxUseCashAccount ? await ResolveCashAccountIdAsync() : null;

        var trade = new Trade(
            Id: Guid.NewGuid(),
            Symbol: TxDivPosition.Symbol,
            Exchange: TxDivPosition.Exchange,
            Name: divName,
            Type: TradeType.CashDividend,
            TradeDate: tradeDate,
            Price: perShare,
            Quantity: (int)TxDivPosition.Quantity,
            RealizedPnl: null,
            RealizedPnlPct: null,
            CashAmount: total,
            CashAccountId: cashAccId,
            Note: null);

        await _txService.RecordAsync(trade);

        await WriteFeeTradeIfAnyAsync(fee, cashAccId, tradeDate,
            $"{divName} 股息手續費", null, trade.Id);

        await ReloadAccountBalancesAsync();
        CloseTxDialog();
        await LoadTradesAsync();
    }

    private async Task ConfirmStockDivAsync()
    {
        if (TxStockDivPosition is null)
        { TxError = "請選擇股票"; return; }
        if (!ParseHelpers.TryParseInt(TxStockDivNewShares, out var newShares) || newShares <= 0)
        { TxError = "配股數無效"; return; }

        var divName = string.IsNullOrEmpty(TxStockDivPosition.Name)
                      ? TxStockDivPosition.Symbol
                      : TxStockDivPosition.Name;

        var trade = new Trade(
            Id: Guid.NewGuid(),
            Symbol: TxStockDivPosition.Symbol,
            Exchange: TxStockDivPosition.Exchange,
            Name: divName,
            Type: TradeType.StockDividend,
            TradeDate: DateTime.SpecifyKind(TxDate, DateTimeKind.Local).ToUniversalTime(),
            Price: 0,
            Quantity: newShares,
            RealizedPnl: null,
            RealizedPnlPct: null,
            CashAmount: null,
            CashAccountId: null,
            Note: null,
            PortfolioEntryId: TxStockDivPosition.Id);  // link to the lot that received the shares

        await _tradeRepo.AddAsync(trade);

        // 更新持倉股數（數量由 trade log 投影，只更新 display row）
        var row = Positions.FirstOrDefault(p => p.AllEntryIds.Contains(TxStockDivPosition.Id));
        if (row is not null)
        {
            row.Quantity += newShares;
            row.Refresh();
            RebuildTotals();
        }

        CloseTxDialog();
        await LoadTradesAsync();
    }

    /// <summary>存入/提款 → 現金帳戶餘額增減。可選填手續費（跨行匯款費、ATM 費等）。</summary>
    private async Task ConfirmCashFlowAsync(TradeType type)
    {
        if (!ParseHelpers.TryParseDecimal(TxAmount, out var amount) || amount <= 0)
        { TxError = "金額無效"; return; }
        var fee = ParseOptionalFee(out var feeError);
        if (feeError is not null)
        { TxError = feeError; return; }

        var cashAccId = await ResolveCashAccountIdAsync();
        if (cashAccId is null)
        { TxError = "請選擇帳戶"; return; }
        var accountName = TxCashAccount?.Name ?? TxCashAccountName.Trim();

        var tradeDate = DateTime.SpecifyKind(TxDate, DateTimeKind.Local).ToUniversalTime();
        var cleanNote = string.IsNullOrWhiteSpace(TxNote) ? null : TxNote;

        var trade = new Trade(
            Id: Guid.NewGuid(), Symbol: accountName, Exchange: "",
            Name: accountName, Type: type,
            TradeDate: tradeDate,
            Price: 0, Quantity: 1, RealizedPnl: null, RealizedPnlPct: null,
            CashAmount: amount, CashAccountId: cashAccId,
            Note: cleanNote);
        await _txService.RecordAsync(trade);

        await WriteFeeTradeIfAnyAsync(fee, cashAccId, tradeDate,
            $"{accountName} 手續費", cleanNote, trade.Id);

        await ReloadAccountBalancesAsync();
        CloseTxDialog();
        await LoadTradesAsync();
    }

    // Shared fee helpers

    /// <summary>
    /// 解析 <see cref="TxFee"/> 為正小數；空白/0 視為無手續費（回 0、err 為 null）。
    /// 負數或非數字回傳 null fee + err 訊息，呼叫端應 set TxError 並 return。
    /// </summary>
    private async Task AutoFillLoanRepayAsync(string label)
    {
        if (string.IsNullOrWhiteSpace(label)) return;

        var row = Liabilities.FirstOrDefault(r =>
            string.Equals(r.Label, label.Trim(), StringComparison.OrdinalIgnoreCase));
        if (row is null || !row.IsLoan) return;

        if (!row.IsScheduleLoaded)
            await LoadLoanScheduleAsync(row);

        var next = row.NextUnpaidEntry;
        if (next is null) return;

        TxPrincipal     = next.PrincipalAmount.ToString("F0");
        TxInterestPaid  = next.InterestAmount > 0 ? next.InterestAmount.ToString("F0") : string.Empty;
    }

    private decimal ParseOptionalFee(out string? err)
    {
        err = null;
        if (string.IsNullOrWhiteSpace(TxFee))
            return 0m;
        if (!ParseHelpers.TryParseDecimal(TxFee, out var fee) || fee < 0)
        {
            err = "手續費無效";
            return 0m;
        }
        return fee;
    }

    /// <summary>
    /// 若 fee &gt; 0，建一筆 <see cref="TradeType.Withdrawal"/>（CashAmount=fee，
    /// 透過 projection 使該現金帳戶扣除對應金額）。
    /// <para>
    /// <paramref name="parentTradeId"/> 設為主交易的 Id，使此費用記錄成為
    /// 主交易的子記錄（<see cref="Trade.ParentTradeId"/>）。
    /// 主交易刪除時，<see cref="ITradeRepository.RemoveChildrenAsync"/> 會一起清掉此筆。
    /// </para>
    /// No-op 當 fee &lt;= 0。
    /// </summary>
    private async Task WriteFeeTradeIfAnyAsync(
        decimal fee, Guid? cashAccountId, DateTime tradeDate,
        string notePrefix, string? userNote, Guid parentTradeId)
    {
        if (fee <= 0)
            return;
        var feeTrade = new Trade(
            Id: Guid.NewGuid(), Symbol: string.Empty, Exchange: string.Empty,
            Name: "手續費", Type: TradeType.Withdrawal,
            TradeDate: tradeDate,
            Price: 0, Quantity: 1, RealizedPnl: null, RealizedPnlPct: null,
            CashAmount: fee, CashAccountId: cashAccountId,
            Note: string.IsNullOrWhiteSpace(userNote)
                  ? notePrefix
                  : $"{notePrefix} — {userNote}",
            ParentTradeId: parentTradeId);
        await _txService.RecordAsync(feeTrade);
    }

    /// <summary>
    /// 借款/還款 → 負債帳戶與現金帳戶同步調整；可選填手續費（AscentPortfolio pattern）。
    /// <list type="bullet">
    /// <item><description><b>借款</b>：Balance +amount、OriginalAmount +amount；若指定現金帳戶則
    /// 現金 +amount（入帳）。若手續費 &gt; 0，另建一筆 Withdrawal，現金再扣 fee
    /// → 淨入帳 = amount − fee，對上銀行實際撥款</description></item>
    /// <item><description><b>還款</b>：Balance −amount；若指定現金帳戶則現金 −amount（扣款）。
    /// 若手續費 &gt; 0，另建 Withdrawal，現金再扣 fee → 總扣款 = amount + fee</description></item>
    /// </list>
    /// 注意：還款不自動拆本息。Balance 長期會反映「累積還的金額」而非「本金餘額」；若要
    /// 精準追蹤本金，使用 LoanRepay 的 Principal / InterestPaid 拆分欄位（不影響 Balance）。
    /// </summary>
    private async Task ConfirmLoanAsync(TradeType type)
    {
        // ── 金額解析（LoanBorrow vs LoanRepay 走不同欄位）────────────────
        decimal cashAmount;
        decimal? principal = null;
        decimal? interestPaid = null;

        if (type == TradeType.LoanBorrow)
        {
            if (!ParseHelpers.TryParseDecimal(TxAmount, out var amt) || amt <= 0)
            { TxError = "金額無效"; return; }
            cashAmount = amt;
        }
        else // LoanRepay
        {
            if (!ParseHelpers.TryParseDecimal(TxPrincipal, out var p) || p < 0)
            { TxError = "本金無效"; return; }
            var rawInterest = string.IsNullOrWhiteSpace(TxInterestPaid) ? "0" : TxInterestPaid;
            if (!ParseHelpers.TryParseDecimal(rawInterest, out var ip) || ip < 0)
            { TxError = "利息金額無效"; return; }
            if (p + ip <= 0)
            { TxError = "本金與利息合計不得為零"; return; }
            principal = p;
            interestPaid = ip > 0 ? ip : null;
            cashAmount = p + ip;
        }

        if (string.IsNullOrWhiteSpace(TxLoanLabel))
        { TxError = "請輸入或選擇貸款名稱"; return; }

        // ── 攤還表欄位（選填；借款時才適用）────────────────────────────────
        decimal? amortAnnualRate = null;
        int?     amortTermMonths  = null;
        if (type == TradeType.LoanBorrow &&
            !string.IsNullOrWhiteSpace(TxLoanRate) &&
            !string.IsNullOrWhiteSpace(TxLoanTermMonths))
        {
            if (!ParseHelpers.TryParseDecimal(TxLoanRate, out var ratePct) || ratePct < 0)
            { TxError = "年利率無效"; return; }
            if (!ParseHelpers.TryParseInt(TxLoanTermMonths, out var termMo) || termMo <= 0)
            { TxError = "還款期數無效"; return; }
            amortAnnualRate = ratePct / 100m;
            amortTermMonths  = termMo;
        }

        var fee = ParseOptionalFee(out var feeError);
        if (feeError is not null)
        { TxError = feeError; return; }

        var tradeDate = DateTime.SpecifyKind(TxDate, DateTimeKind.Local).ToUniversalTime();
        var liabName = TxLoanLabel.Trim();
        var cleanNote = string.IsNullOrWhiteSpace(TxNote) ? null : TxNote;
        var cashAccId = TxUseCashAccount ? await ResolveCashAccountIdAsync() : null;

        // ── 建立 AssetItem + 攤還表（有填利率與期數時）────────────────────
        if (type == TradeType.LoanBorrow && amortAnnualRate.HasValue && amortTermMonths.HasValue
            && _assetRepo is not null && _loanScheduleRepo is not null)
        {
            var firstPayDate = DateOnly.FromDateTime(TxLoanStartDate);
            decimal? loanFee = fee > 0 ? fee : null;
            var asset = new AssetItem(
                Guid.NewGuid(), liabName, FinancialType.Liability, null, "TWD",
                DateOnly.FromDateTime(DateTime.Today), IsActive: true, UpdatedAt: null,
                LoanAnnualRate: amortAnnualRate, LoanTermMonths: amortTermMonths,
                LoanStartDate:  firstPayDate,   LoanHandlingFee: loanFee);
            await _assetRepo.AddItemAsync(asset);
            var schedule = AmortizationService.Generate(
                asset.Id, cashAmount, amortAnnualRate.Value, amortTermMonths.Value, firstPayDate);
            await _loanScheduleRepo.BulkInsertAsync(schedule);
        }

        var trade = new Trade(
            Id: Guid.NewGuid(), Symbol: liabName, Exchange: string.Empty,
            Name: liabName, Type: type,
            TradeDate: tradeDate,
            Price: 0, Quantity: 1, RealizedPnl: null, RealizedPnlPct: null,
            CashAmount: cashAmount, CashAccountId: cashAccId, Note: cleanNote,
            LoanLabel: liabName,
            Principal: principal,
            InterestPaid: interestPaid);

        await _txService.RecordAsync(trade);
        await WriteFeeTradeIfAnyAsync(fee, cashAccId, tradeDate,
            $"{liabName} 手續費", cleanNote, trade.Id);

        await ReloadAccountBalancesAsync();
        CloseTxDialog();
        await LoadTradesAsync();

        if (amortAnnualRate.HasValue)
        {
            await LoadLiabilitiesAsync();
            RebuildTotals();
        }
    }

    /// <summary>
    /// 轉帳 → 在兩個現金帳戶之間搬錢。
    /// <list type="bullet">
    /// <item><description>
    ///   同幣別（srcAmount == dstAmount）→ 建立單筆 <see cref="TradeType.Transfer"/> 記錄，
    ///   由 <see cref="ITransactionService"/> 同時調整兩端帳戶餘額。
    /// </description></item>
    /// <item><description>
    ///   跨幣別（srcAmount ≠ dstAmount，例如 30,000 TWD → 1,000 USD）→ 維持舊的
    ///   Withdrawal + Deposit pair 格式（Note 互相標記 "轉帳 →/←"），因為原生 Transfer
    ///   記錄只能持有一個 CashAmount，無法表達不同幣別的兩端金額。
    /// </description></item>
    /// </list>
    /// 若有手續費 → 從來源帳戶額外扣（同 ConfirmCashFlow / ConfirmLoan 模式）。
    /// </summary>
    private async Task ConfirmTransferAsync()
    {
        if (!ParseHelpers.TryParseDecimal(TxAmount, out var srcAmount) || srcAmount <= 0)
        { TxError = "轉出金額無效"; return; }
        if (!ParseHelpers.TryParseDecimal(TxTransferTargetAmount, out var dstAmount) || dstAmount <= 0)
        { TxError = "轉入金額無效"; return; }
        if (CashAccounts.Count < 2)
        { TxError = "至少需要兩個帳戶才能轉帳"; return; }
        if (TxCashAccount is null)
        { TxError = "請選擇來源帳戶"; return; }
        var destId = await ResolveTransferTargetIdAsync();
        if (destId is null)
        { TxError = "請選擇轉入帳戶"; return; }
        if (TxCashAccount.Id == destId)
        { TxError = "來源與目標不能是同一個帳戶"; return; }
        var fee = ParseOptionalFee(out var feeError);
        if (feeError is not null)
        { TxError = feeError; return; }

        var tradeDate = DateTime.SpecifyKind(TxDate, DateTimeKind.Local).ToUniversalTime();
        var srcName = TxCashAccount.Name;
        var dstName = TxTransferTarget?.Name ?? TxTransferTargetName.Trim();
        var userNote = string.IsNullOrWhiteSpace(TxNote) ? null : TxNote;

        // feeParentId: 費用子記錄掛在來源交易下（同幣別 = Transfer 主記錄；跨幣別 = Withdrawal）
        Guid feeParentId;

        if (srcAmount == dstAmount)
        {
            // ── 同幣別：單筆原生 Transfer ────────────────────────────────────
            // TransactionService.ApplySideEffectsAsync 會同時：
            //   來源帳戶 − CashAmount（PrimaryCashDelta）
            //   目標帳戶 + CashAmount（ToCashAccountId 分支）
            var transfer = new Trade(
                Id:              Guid.NewGuid(),
                Symbol:          srcName,
                Exchange:        string.Empty,
                Name:            $"{srcName} → {dstName}",
                Type:            TradeType.Transfer,
                TradeDate:       tradeDate,
                Price:           0,
                Quantity:        1,
                RealizedPnl:     null,
                RealizedPnlPct:  null,
                CashAmount:      srcAmount,
                CashAccountId:   TxCashAccount.Id,
                ToCashAccountId: destId.Value,
                Note:            userNote);
            await _txService.RecordAsync(transfer);
            feeParentId = transfer.Id;
        }
        else
        {
            // ── 跨幣別：保留 Withdrawal + Deposit pair ──────────────────────
            var withdrawNote = string.IsNullOrWhiteSpace(userNote)
                ? $"轉帳 → {dstName}"
                : $"轉帳 → {dstName} — {userNote}";
            var withdraw = new Trade(
                Id: Guid.NewGuid(), Symbol: srcName, Exchange: string.Empty,
                Name: srcName, Type: TradeType.Withdrawal,
                TradeDate: tradeDate,
                Price: 0, Quantity: 1, RealizedPnl: null, RealizedPnlPct: null,
                CashAmount: srcAmount, CashAccountId: TxCashAccount.Id, Note: withdrawNote);
            await _txService.RecordAsync(withdraw);
            feeParentId = withdraw.Id;

            var depositNote = string.IsNullOrWhiteSpace(userNote)
                ? $"轉帳 ← {srcName}"
                : $"轉帳 ← {srcName} — {userNote}";
            var deposit = new Trade(
                Id: Guid.NewGuid(), Symbol: dstName, Exchange: string.Empty,
                Name: dstName, Type: TradeType.Deposit,
                TradeDate: tradeDate,
                Price: 0, Quantity: 1, RealizedPnl: null, RealizedPnlPct: null,
                CashAmount: dstAmount, CashAccountId: destId.Value, Note: depositNote);
            await _txService.RecordAsync(deposit);
        }

        // 手續費（從來源帳戶扣，無論哪條路徑）
        await WriteFeeTradeIfAnyAsync(fee, TxCashAccount.Id, tradeDate,
            $"轉帳手續費 ({srcName} → {dstName})", userNote, feeParentId);

        await ReloadAccountBalancesAsync();
        CloseTxDialog();
        await LoadTradesAsync();
    }

}
