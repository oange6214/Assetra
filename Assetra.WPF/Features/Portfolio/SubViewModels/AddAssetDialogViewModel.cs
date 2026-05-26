using Assetra.Application.Portfolio.Contracts;
using Assetra.Application.Portfolio.Dtos;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Assetra.Core.Trading;
using Assetra.WPF.Features.Portfolio;
using Assetra.WPF.Infrastructure;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace Assetra.WPF.Features.Portfolio.SubViewModels;

/// <summary>
/// Holds all state and commands for the "add new asset" dialog panel.
/// Raised <see cref="AssetAdded"/> after every successful add so the parent
/// <see cref="PortfolioViewModel"/> can reload positions, balances, and totals.
/// </summary>
public partial class AddAssetDialogViewModel : ObservableObject
{
    private readonly IAddAssetWorkflowService _addAssetWorkflow;
    private readonly IAccountUpsertWorkflowService _accountUpsertWorkflow;
    private readonly ITransactionWorkflowService? _transactionWorkflow;
    private readonly ICreditCardMutationWorkflowService _creditCardMutationWorkflow;
    private readonly ICreditCardTransactionWorkflowService? _creditCardTransactionWorkflow;
    private readonly ILoanMutationWorkflowService? _loanMutationWorkflow;
    private readonly ILocalizationService? _localization;

    /// <summary>
    /// Raised after every successful add (stock buy, crypto, non-stock, cash account).
    /// The parent ViewModel subscribes and calls its reload pipeline in response.
    /// </summary>
    public event EventHandler? AssetAdded;

    // ── Services injected from the parent for fields the Tx dialog shares ────────────
    // Replaces the former 7-Func service-locator pattern with a single typed
    // context interface (IBuyExecutionContext). The sub-VM still doesn't hold a
    // back-reference to PortfolioViewModel — production wiring uses a thin
    // adapter (TransactionBuyContext) defined alongside the parent VM.

    /// <summary>
    /// Read-only access to the transaction-dialog state needed during a buy.
    /// Defaults to <see cref="NullBuyContext.Instance"/> so callers / fixtures
    /// that don't exercise the buy path don't have to wire anything.
    /// </summary>
    public IBuyExecutionContext BuyContext { get; set; } = NullBuyContext.Instance;

    public AddAssetDialogViewModel(
        IAddAssetWorkflowService addAssetWorkflow,
        IAccountUpsertWorkflowService accountUpsertWorkflow,
        ICreditCardMutationWorkflowService creditCardMutationWorkflow)
        : this(addAssetWorkflow, accountUpsertWorkflow, null, creditCardMutationWorkflow)
    {
    }

    public AddAssetDialogViewModel(
        IAddAssetWorkflowService addAssetWorkflow,
        IAccountUpsertWorkflowService accountUpsertWorkflow,
        ITransactionWorkflowService? transactionWorkflow,
        ICreditCardMutationWorkflowService creditCardMutationWorkflow,
        ICreditCardTransactionWorkflowService? creditCardTransactionWorkflow = null,
        ILoanMutationWorkflowService? loanMutationWorkflow = null,
        ILocalizationService? localization = null)
    {
        _addAssetWorkflow = addAssetWorkflow;
        _accountUpsertWorkflow = accountUpsertWorkflow;
        _transactionWorkflow = transactionWorkflow;
        _creditCardMutationWorkflow = creditCardMutationWorkflow;
        _creditCardTransactionWorkflow = creditCardTransactionWorkflow;
        _loanMutationWorkflow = loanMutationWorkflow;
        _localization = localization;
    }

    // ── Dialog visibility ────────────────────────────────────────────────────────────

    [ObservableProperty] private bool _isAddDialogOpen;
    [ObservableProperty] private bool _addDialogIsInvestmentMode = true;
    [ObservableProperty] private string _addDialogMode = "account";

    /// <summary>
    /// Portfolio-Groups-Refactor P3 — TransactionDialogViewModel copies its own
    /// SelectedPortfolioGroup into here before invoking ConfirmAddCommand so the
    /// Buy request DTO carries the user's group choice. null = sealed-DefaultId by repo.
    /// </summary>
    public Guid? SelectedPortfolioGroupId { get; set; }

    /// <summary>True while the user is on the type-picker step (liability mode only).</summary>
    [ObservableProperty] private bool _isTypePickerStep;

    public bool IsFormStep => !IsTypePickerStep;

    /// <summary>True when the dialog is on its form step in a mode that came from the picker (back arrow visible).</summary>
    public bool IsLiabilityFormStep =>
        IsFormStep && (AddDialogMode == "liability" || AddDialogMode == "account");

    partial void OnIsTypePickerStepChanged(bool value)
    {
        OnPropertyChanged(nameof(IsFormStep));
        OnPropertyChanged(nameof(IsLiabilityFormStep));
    }

    // ── Asset type ───────────────────────────────────────────────────────────────────

    [ObservableProperty] private string _addAssetType = "stock";
    // "stock" | "fund" | "metal" | "bond" | "crypto" | "cash" | "liability"

    /// <summary>Free-form preset label (e.g., 房貸/車貸/0% 分期). Set by picker, editable by user.</summary>
    [ObservableProperty] private string _addSubtype = string.Empty;

    // ── Stock / ETF fields ───────────────────────────────────────────────────────────

    [ObservableProperty] private DateTime _addBuyDate = DateTime.Today;
    [ObservableProperty] private string _addSymbol = string.Empty;
    [ObservableProperty] private string _addExchange = string.Empty;
    [ObservableProperty] private string _addSymbolName = string.Empty;
    [ObservableProperty] private string _addSymbolCurrency = string.Empty;
    [ObservableProperty] private string _addPrice = string.Empty;
    [ObservableProperty] private string _addQuantity = string.Empty;
    [ObservableProperty] private string _addError = string.Empty;

    // ── Field validation messages (empty string = no error) ─────────────────────────

    [ObservableProperty] private string _addPriceError = string.Empty;
    [ObservableProperty] private string _addQuantityError = string.Empty;
    [ObservableProperty] private string _addCostError = string.Empty;
    [ObservableProperty] private string _addCryptoQtyError = string.Empty;
    [ObservableProperty] private string _addCryptoPriceError = string.Empty;

    // ── Close price auto-fill state ──────────────────────────────────────────────────

    [ObservableProperty] private bool _isLoadingClosePrice;
    [ObservableProperty] private string _closePriceHint = string.Empty;

    // ── Buy preview live calculation ─────────────────────────────────────────────────

    [ObservableProperty] private decimal _addGrossAmount;
    [ObservableProperty] private decimal _addCommission;
    [ObservableProperty] private decimal _addTotalCost;
    [ObservableProperty] private decimal _addCostPerShare;

    public bool HasAddPreview => AddTotalCost > 0;
    partial void OnAddTotalCostChanged(decimal _) => OnPropertyChanged(nameof(HasAddPreview));

    // ── Symbol autocomplete ──────────────────────────────────────────────────────────

    [ObservableProperty] private bool _isSuggestionsOpen;
    [ObservableProperty] private StockSearchResult? _selectedSuggestion;

    // IReadOnlyList instead of ObservableCollection: one PropertyChanged per update
    // rather than Clear() + N×Add() each triggering a separate ListBox layout pass.
    [ObservableProperty] private IReadOnlyList<StockSearchResult> _symbolSuggestions = [];

    partial void OnSelectedSuggestionChanged(StockSearchResult? value)
    {
        if (value is null)
            return;
        SelectSuggestion(value);
        SelectedSuggestion = null;
    }

    private bool _suppressSuggestions;
    private bool _suppressClosePriceAutoFill;
    // Exposed so that PortfolioViewModel.Transactions.cs can suppress the popup
    // when pre-filling AddSymbol for the Tx dialog's Buy flow (edit path).
    public bool SuppressSuggestions
    {
        get => _suppressSuggestions;
        set => _suppressSuggestions = value;
    }

    // Exposed so edit-mode prefill can keep the persisted trade price stable instead
    // of asynchronously replacing it with a looked-up historical close after the dialog opens.
    public bool SuppressClosePriceAutoFill
    {
        get => _suppressClosePriceAutoFill;
        set
        {
            _suppressClosePriceAutoFill = value;
            if (!value)
                return;

            _closePriceCts?.Cancel();
            IsLoadingClosePrice = false;
            ClosePriceHint = string.Empty;
        }
    }

    partial void OnAddSymbolChanged(string value)
    {
        if (_suppressSuggestions)
            return;
        AddExchange = string.Empty;
        AddSymbolName = string.Empty;
        AddSymbolCurrency = string.Empty;
        if (!AddTypeIsStock || string.IsNullOrWhiteSpace(value))
        {
            IsSuggestionsOpen = false;
            SymbolSuggestions = [];
            ClosePriceHint = string.Empty;
            return;
        }
        SymbolSuggestions = _addAssetWorkflow.SearchSymbols(value.Trim());
        IsSuggestionsOpen = SymbolSuggestions.Count > 0;
        TriggerClosePriceFetch();
    }

    partial void OnAddBuyDateChanged(DateTime _)
    {
        if (SuppressClosePriceAutoFill)
            return;
        TriggerClosePriceFetch();
    }

    private void SelectSuggestion(StockSearchResult suggestion)
    {
        _suppressSuggestions = true;
        IsSuggestionsOpen = false;
        AddSymbol = suggestion.Symbol;
        AddExchange = suggestion.Exchange;
        AddSymbolName = suggestion.Name;
        AddSymbolCurrency = suggestion.Currency;
        _suppressSuggestions = false;
        TriggerClosePriceFetch();
    }

    // ── Close price fetch ────────────────────────────────────────────────────────────

    private CancellationTokenSource? _closePriceCts;

    private void TriggerClosePriceFetch()
    {
        if (SuppressClosePriceAutoFill)
            return;
        if (!AddTypeIsStock || string.IsNullOrWhiteSpace(AddSymbol))
        {
            ClosePriceHint = string.Empty;
            return;
        }
        _closePriceCts?.Cancel();
        _closePriceCts = new CancellationTokenSource();
        _ = FetchClosePriceAsync(AddSymbol.Trim().ToUpper(), AddBuyDate, _closePriceCts.Token);
    }

    /// <summary>
    /// P2.4 — XAML 「⚡ 取得市價」inline link 用。手動觸發 close price 抓取，
    /// 跳過 AutoFill 抑制旗標（使用者明確點下去就是要刷新）。
    /// </summary>
    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void FetchMarketPrice()
    {
        if (string.IsNullOrWhiteSpace(AddSymbol)) return;
        _closePriceCts?.Cancel();
        _closePriceCts = new CancellationTokenSource();
        var previousSuppress = SuppressClosePriceAutoFill;
        SuppressClosePriceAutoFill = false;
        _ = FetchClosePriceAsync(AddSymbol.Trim().ToUpper(), AddBuyDate, _closePriceCts.Token)
            .ContinueWith(_ => SuppressClosePriceAutoFill = previousSuppress,
                TaskScheduler.FromCurrentSynchronizationContext());
    }

    private async Task FetchClosePriceAsync(string symbol, DateTime buyDate, CancellationToken ct)
    {
        try
        {
            if (SuppressClosePriceAutoFill)
                return;
            IsLoadingClosePrice = true;
            ClosePriceHint = string.Empty;
            var result = await _addAssetWorkflow.LookupClosePriceAsync(symbol, buyDate, EmptyToNull(AddExchange), ct);
            if (ct.IsCancellationRequested || SuppressClosePriceAutoFill)
                return;

            if (result.HasPrice && result.Price.HasValue)
            {
                AddPrice = result.Price.Value.ToString("0.##");
                ClosePriceHint = result.Hint;
            }
            else
            {
                ClosePriceHint = result.Hint;
            }
        }
        catch (OperationCanceledException) { /* normal cancellation */ }
        catch (Exception ex)
        {
            // H5: previously the catch swallowed the exception silently with a
            // hard-coded zh-TW hint — invisible in logs and broken for en-US.
            Log.Warning(ex, "Close-price fetch failed for {Symbol}", AddSymbol);
            ClosePriceHint = _localization?.Get("AddAsset.ClosePrice.FetchFailed",
                "收盤價查詢失敗，請手動輸入") ?? "收盤價查詢失敗，請手動輸入";
        }
        finally { IsLoadingClosePrice = false; }
    }

    // ── Buy preview live calculation ─────────────────────────────────────────────────

    partial void OnAddPriceChanged(string value)
    {
        AddPriceError = ValidatePositiveDecimalOrEmpty(value);
        UpdateBuyPreview();
    }

    partial void OnAddQuantityChanged(string value)
    {
        AddQuantityError = ValidatePositiveIntOrEmpty(value);
        UpdateBuyPreview();
    }

    public void UpdateBuyPreview()
    {
        if (!ParseHelpers.TryParseDecimal(AddPrice, out var price) || price <= 0 ||
            !ParseHelpers.TryParseInt(AddQuantity, out var qty) || qty <= 0)
        {
            AddGrossAmount = 0;
            AddCommission = 0;
            AddTotalCost = 0;
            AddCostPerShare = 0;
            return;
        }

        // Total-mode + "金額已含手續費" → record Trade with Commission=0 so the
        // total cash out equals exactly what the user typed. Pass manualFee=0
        // here so the preview also reflects this (no double-fee illusion).
        decimal? overrideManualFee = null;
        if (BuyContext.BuyIsTotalMode && BuyContext.BuyTotalIncludesFee)
        {
            overrideManualFee = 0m;
        }
        else
        {
            var txFee = BuyContext.TxFee;
            if (!string.IsNullOrWhiteSpace(txFee) &&
                ParseHelpers.TryParseDecimal(txFee, out var manualFee) && manualFee >= 0)
            {
                overrideManualFee = manualFee;
            }
        }

        var preview = _addAssetWorkflow.BuildBuyPreview(new BuyPreviewRequest(
            AddSymbol.Trim(),
            price,
            qty,
            BuyContext.CommissionDiscount,
            overrideManualFee,
            EmptyToNull(AddExchange)));

        AddGrossAmount = preview.GrossAmount;
        AddCommission = preview.Commission;
        AddTotalCost = preview.TotalCost;
        AddCostPerShare = preview.CostPerShare;
    }

    // ── Non-stock investment fields (fund / precious-metal / bond) ───────────────────

    [ObservableProperty] private string _addName = string.Empty;
    [ObservableProperty] private string _addCost = string.Empty;

    partial void OnAddCostChanged(string value) =>
        AddCostError = ValidatePositiveDecimalOrEmpty(value);

    // ── Crypto fields ────────────────────────────────────────────────────────────────

    [ObservableProperty] private string _addCryptoSymbol = string.Empty;
    [ObservableProperty] private string _addCryptoQty = string.Empty;
    [ObservableProperty] private string _addCryptoPrice = string.Empty;

    partial void OnAddCryptoQtyChanged(string value) =>
        AddCryptoQtyError = ValidatePositiveDecimalOrEmpty(value);

    partial void OnAddCryptoPriceChanged(string value) =>
        AddCryptoPriceError = ValidatePositiveDecimalOrEmpty(value);

    // ── Cash account field ───────────────────────────────────────────────────────────

    [ObservableProperty] private string _addAccountName = string.Empty;

    /// <summary>
    /// Currency for the new cash account. Defaults to "TWD" but the WPF parent
    /// VM resets it to <c>AppSettings.PrimaryCurrency</c> when the dialog opens
    /// so multi-currency users don't have to change it on every account.
    /// Sourced from <see cref="AccountDialogViewModel.SupportedCurrencies"/>
    /// for the dropdown.
    /// </summary>
    [ObservableProperty] private string _addAccountCurrency = "TWD";
    [ObservableProperty] private bool _addInitialDepositEnabled;
    [ObservableProperty] private string _addInitialDepositAmount = string.Empty;
    [ObservableProperty] private DateTime? _addInitialDepositDate = DateTime.Today;
    [ObservableProperty] private string _addInitialDepositNote = string.Empty;

    // ── Credit card fields ───────────────────────────────────────────────────────────

    [ObservableProperty] private string _addCreditCardName = string.Empty;
    [ObservableProperty] private string _addCreditCardIssuer = string.Empty;
    [ObservableProperty] private string _addCreditCardBillingDay = string.Empty;
    [ObservableProperty] private string _addCreditCardDueDay = string.Empty;
    [ObservableProperty] private string _addCreditCardLimit = string.Empty;
    [ObservableProperty] private bool _addInitialCreditCardBalanceEnabled;
    [ObservableProperty] private string _addInitialCreditCardBalanceAmount = string.Empty;
    [ObservableProperty] private DateTime? _addInitialCreditCardBalanceDate = DateTime.Today;
    [ObservableProperty] private string _addInitialCreditCardBalanceNote = string.Empty;

    // ── Loan fields ──────────────────────────────────────────────────────────────────

    [ObservableProperty] private string _addLoanName = string.Empty;
    [ObservableProperty] private string _addLoanAmount = string.Empty;
    [ObservableProperty] private string _addLoanAnnualRate = string.Empty;
    [ObservableProperty] private string _addLoanTermMonths = string.Empty;
    [ObservableProperty] private DateTime? _addLoanStartDate = DateTime.Today;
    [ObservableProperty] private string _addLoanHandlingFee = string.Empty;
    [ObservableProperty] private CashAccountRowViewModel? _selectedLoanCashAccount;

    // ── Asset type toggle helpers (for RadioButton IsChecked TwoWay) ─────────────────

    public bool AddTypeIsStock { get => AddAssetType == "stock"; set { if (value) AddAssetType = "stock"; } }
    public bool AddTypeIsFund { get => AddAssetType == "fund"; set { if (value) AddAssetType = "fund"; } }
    public bool AddTypeIsMetal { get => AddAssetType == "metal"; set { if (value) AddAssetType = "metal"; } }
    public bool AddTypeIsBond { get => AddAssetType == "bond"; set { if (value) AddAssetType = "bond"; } }
    public bool AddTypeIsCrypto { get => AddAssetType == "crypto"; set { if (value) AddAssetType = "crypto"; } }
    public bool AddTypeIsAccount { get => AddAssetType == "cash"; set { if (value) AddAssetType = "cash"; } }
    public bool AddTypeIsLoan { get => AddAssetType == "loan"; set { if (value) AddAssetType = "loan"; } }
    public bool AddTypeIsCreditCard { get => AddAssetType == "creditCard"; set { if (value) AddAssetType = "creditCard"; } }
    public bool IsAccountDialogMode => AddDialogMode == "account";
    public bool IsLiabilityDialogMode => AddDialogMode == "liability";
    public IReadOnlyList<CashAccountRowViewModel> LoanCashAccountOptions => GetCashAccounts();

    public Func<IReadOnlyList<CashAccountRowViewModel>> GetCashAccounts { get; set; } = static () => [];

    // Fund / precious-metal / bond share "name + total cost" form
    public bool AddTypeIsNonStockInvestment => AddAssetType is "fund" or "metal" or "bond";

    // Crypto-specific form (symbol + qty + unit price)
    public bool AddTypeIsCryptoForm => AddAssetType == "crypto";

    partial void OnAddAssetTypeChanged(string _)
    {
        OnPropertyChanged(nameof(AddTypeIsStock));
        OnPropertyChanged(nameof(AddTypeIsFund));
        OnPropertyChanged(nameof(AddTypeIsMetal));
        OnPropertyChanged(nameof(AddTypeIsBond));
        OnPropertyChanged(nameof(AddTypeIsCrypto));
        OnPropertyChanged(nameof(AddTypeIsAccount));
        OnPropertyChanged(nameof(AddTypeIsLoan));
        OnPropertyChanged(nameof(AddTypeIsCreditCard));
        OnPropertyChanged(nameof(AddTypeIsNonStockInvestment));
        OnPropertyChanged(nameof(AddTypeIsCryptoForm));
        AddError = string.Empty;
    }

    partial void OnAddDialogModeChanged(string _)
    {
        OnPropertyChanged(nameof(IsAccountDialogMode));
        OnPropertyChanged(nameof(IsLiabilityDialogMode));
        OnPropertyChanged(nameof(IsLiabilityFormStep));
    }

    // ── Commands ─────────────────────────────────────────────────────────────────────

    [RelayCommand]
    private void CloseAddDialog()
    {
        IsSuggestionsOpen = false;
        IsAddDialogOpen = false;
    }

    /// <summary>Liability picker step: user picks a preset and advances to the form.</summary>
    /// <param name="kind">Preset key. Either a base type ("loan" / "creditCard") or "{base}:{label}" pair.</param>
    [RelayCommand]
    private void SelectLiabilityType(string kind)
    {
        if (string.IsNullOrEmpty(kind)) return;

        var sep = kind.IndexOf(':');
        var baseType = sep < 0 ? kind : kind[..sep];
        var defaultSubtype = sep < 0 ? string.Empty : kind[(sep + 1)..];

        AddAssetType = baseType;
        AddSubtype = defaultSubtype;
        AddError = string.Empty;
        IsTypePickerStep = false;
    }

    /// <summary>Returns from the form to the type-picker step without losing dialog open state.</summary>
    [RelayCommand]
    private void BackToTypePicker()
    {
        AddError = string.Empty;
        IsTypePickerStep = true;
    }

    [RelayCommand]
    private async Task ConfirmAdd()
    {
        switch (AddAssetType)
        {
            case "stock":
                await AddStockAsync();
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
            case "cash":
                await AddAccountAsync();
                break;
            case "loan":
                await AddLoanAsync();
                break;
            case "creditCard":
                await AddCreditCardAsync();
                break;
        }
    }

    // ── Stock / ETF ───────────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task AddPosition()
    {
        // INVARIANT (Task 18 Option B): when Buy.MetaOnly is true we MUST NOT write a
        // Trade row. Callers rely on this to pre-register symbols without affecting P&L.
        if (BuyContext.BuyMetaOnly)
        {
            AddError = string.Empty;
            if (string.IsNullOrWhiteSpace(AddSymbol))
            { AddError = "請輸入股票代號"; return; }

            var metaSymbol = AddSymbol.Trim().ToUpper();
            await _addAssetWorkflow
                .EnsureStockEntryAsync(new EnsureStockEntryRequest(
                    metaSymbol,
                    EmptyToNull(AddExchange),
                    EmptyToNull(AddSymbolName)))
                .ConfigureAwait(true);

            // No trade written. Clear input + refresh via AssetAdded event so the
            // parent reloads and the new Qty=0 row appears (when HideEmptyPositions is off).
            AddSymbol = string.Empty;
            AddPrice = string.Empty;
            AddQuantity = string.Empty;
            ClosePriceHint = string.Empty;
            IsAddDialogOpen = false;

            AssetAdded?.Invoke(this, EventArgs.Empty);
            return;
        }

        AddError = string.Empty;
        if (string.IsNullOrWhiteSpace(AddSymbol))
        { AddError = "請輸入股票代號"; return; }
        if (!ParseHelpers.TryParseInt(AddQuantity, out var qty) || qty <= 0)
        { AddError = "股數無效"; return; }

        // MultiCurrency-Trade-Refactor P3 Mode C — 成交價現在是「optional 如果 ActualCash 有填」。
        // 先 try-parse；若空但 ActualCashAmount + Qty 已知 (+ FxRate 對跨幣別)，下方會反推一個。
        var settlementMode = string.Equals(BuyContext.SettlementInputMode, "fx", StringComparison.OrdinalIgnoreCase)
            ? "fx"
            : "statement";
        var isCrossCurrencyCash = BuyContext.UseCashAccount && IsCrossCurrencyCashDebit();

        var hasPrice = ParseHelpers.TryParseDecimal(AddPrice, out var price) && price > 0;
        if (!hasPrice && (!isCrossCurrencyCash || settlementMode != "statement" || string.IsNullOrWhiteSpace(BuyContext.ActualCashAmount)))
        {
            AddError = "成交價無效（或請填帳戶扣款金額讓系統反推）";
            return;
        }

        var symbol = AddSymbol.Trim().ToUpper();

        // Total-mode + "金額已含手續費" → force commission=0 so the recorded
        // Trade.CashAmount equals exactly the user-typed total (price × qty).
        // Otherwise resolve manualFee from the optional TxFee field (may be
        // null → service auto-computes via TaiwanTradeFeeCalculator).
        decimal? manualFee;
        if (BuyContext.BuyIsTotalMode && BuyContext.BuyTotalIncludesFee)
        {
            manualFee = 0m;
        }
        else
        {
            var txFee = BuyContext.TxFee;
            if (!TryResolveManualFee(txFee, out manualFee))
            { AddError = "手續費無效"; return; }
        }

        decimal? parsedActualCashAmount = null;
        if (!string.IsNullOrWhiteSpace(BuyContext.ActualCashAmount))
        {
            if (!ParseHelpers.TryParseDecimal(BuyContext.ActualCashAmount, out var parsedActual) ||
                parsedActual <= 0)
            {
                AddError = "帳戶扣款金額（實際扣款）無效";
                return;
            }

            parsedActualCashAmount = parsedActual;
        }

        // P3 — 跨幣別交易解析 FX rate。空字串視為 null（同幣別或使用者沒填）。
        decimal? parsedFxRate = null;
        if (!string.IsNullOrWhiteSpace(BuyContext.FxRate))
        {
            if (!ParseHelpers.TryParseDecimal(BuyContext.FxRate, out var parsedFx) || parsedFx <= 0)
            {
                AddError = "匯率無效";
                return;
            }
            parsedFxRate = parsedFx;
        }

        var actualCashAmount = isCrossCurrencyCash && settlementMode == "statement"
            ? parsedActualCashAmount
            : null;
        var fxRate = isCrossCurrencyCash && settlementMode == "fx"
            ? parsedFxRate
            : null;

        // 跨幣別現金連動時，只有目前使用者選定的結算輸入模式是權威。
        // statement mode: 帳戶/券商明細上的扣款金額是權威，匯率只可由系統反推。
        // fx mode: 匯率是權威，帳戶扣款由成交資料估算。
        if (isCrossCurrencyCash && settlementMode == "statement" && actualCashAmount is null)
        {
            AddError = "跨幣別買入請填寫帳戶扣款金額（實際扣款），或改用匯率估算";
            return;
        }
        if (isCrossCurrencyCash && settlementMode == "fx" && fxRate is null)
        {
            AddError = "跨幣別買入請填寫或取得匯率，或改用明細金額";
            return;
        }

        // P3 Mode C — 「只知道扣款金額 + 股數」快速輸入：當 Price 為空 + ActualCash 有填時，
        // 從現金反推 Price。對同幣別交易直接除；對跨幣別則需要 FxRate（用 1.0 fallback
        // 若兩個都沒填，避免靜默用錯匯率產生荒謬成本均價）。手續費粗估：留空時當 0
        // （適用券商帳戶扣款已含手續費的多數情境，跟「金額已含手續費」的精神一致）。
        if (!hasPrice && actualCashAmount is { } cash && qty > 0)
        {
            if (isCrossCurrencyCash && fxRate is null)
            {
                AddError = "跨幣別反推單價需要匯率";
                return;
            }
            var feeForPrice = manualFee ?? 0m;
            var grossNative = cash - feeForPrice;
            if (grossNative <= 0)
            {
                AddError = "帳戶扣款金額（實際扣款）不足以扣除手續費，請檢查";
                return;
            }
            var rateForPrice = fxRate ?? 1m;
            if (rateForPrice <= 0)
            {
                AddError = "跨幣別反推單價需要匯率";
                return;
            }
            price = grossNative / qty / rateForPrice;
            hasPrice = true;

            // ⚠ 寫回 AddPrice 讓 UI 顯示推算值（使用者下次能看到），但不寫回 AddCommission /
            // AddTotalCost 因為 preview 走另一條路；只更新 AddPrice 影響有限。
            AddPrice = price.ToString("F4", System.Globalization.CultureInfo.InvariantCulture);
        }

        // P3 — 雙保險：若兩者皆填，ActualCashAmount 為權威。如果只填一個，
        // 自動反推另一個寫入 DB，未來報表想還原 USD 成本基礎不需要重新查匯率。
        // 反推公式：CashAmount ≈ Price × Qty × FxRate + Commission (僅 estimate；
        // 不去動 commission 換匯，多數情境誤差可接受)。
        if (actualCashAmount is null && fxRate is { } fxOnly && qty > 0 && price > 0)
        {
            // 從匯率推算 cash amount（含預估手續費；commission 取自 preview）
            actualCashAmount = price * qty * fxOnly + (manualFee ?? 0m);
        }
        else if (fxRate is null && actualCashAmount is { } cashOnly && qty > 0 && price > 0)
        {
            // 從現金扣款 + 估算手續費反推匯率：(cash − fee) / (price × qty)
            var grossInFunding = cashOnly - (manualFee ?? 0m);
            if (grossInFunding > 0)
                fxRate = grossInFunding / (price * qty);
        }

        var cashAccId = BuyContext.UseCashAccount ? BuyContext.CashAccountId : null;
        var settlementCurrency = BuyContext.UseCashAccount && !string.IsNullOrWhiteSpace(BuyContext.SettlementCurrency)
            ? BuyContext.SettlementCurrency.Trim().ToUpperInvariant()
            : BuyContext.UseCashAccount && !string.IsNullOrWhiteSpace(BuyContext.CashAccountCurrency)
                ? BuyContext.CashAccountCurrency.Trim().ToUpperInvariant()
                : null;
        var fxSource = BuyContext.IsFxManual
            ? "manual"
            : string.IsNullOrWhiteSpace(BuyContext.FxSource)
                ? null
                : BuyContext.FxSource.Trim();

        await _addAssetWorkflow.ExecuteStockBuyAsync(new StockBuyRequest(
            Symbol: symbol,
            Price: price,
            Quantity: qty,
            BuyDate: AddBuyDate,
            CashAccountId: cashAccId,
            CommissionDiscount: BuyContext.CommissionDiscount,
            ManualFee: manualFee,
            Exchange: EmptyToNull(AddExchange),
            Name: EmptyToNull(AddSymbolName),
            ActualCashAmount: actualCashAmount,
            FxRate: fxRate,
            SettlementCurrency: settlementCurrency,
            FxRateDate: BuyContext.FxRateDate,
            FxSource: fxSource,
            PortfolioGroupId: SelectedPortfolioGroupId));

        // Cash balance reflects the Buy trade written above via the projection service;
        // no manual AdjustCashAccountAsync needed (single-truth architecture).

        AddSymbol = string.Empty;
        AddExchange = string.Empty;
        AddSymbolName = string.Empty;
        AddSymbolCurrency = string.Empty;
        AddPrice = string.Empty;
        AddQuantity = string.Empty;
        AddGrossAmount = 0;
        AddCommission = 0;
        AddTotalCost = 0;
        ClosePriceHint = string.Empty;
        IsAddDialogOpen = false;

        AssetAdded?.Invoke(this, EventArgs.Empty);
    }

    private Task AddStockAsync() => AddPosition();

    private static string? EmptyToNull(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private bool IsCrossCurrencyCashDebit()
    {
        var instrumentCurrency = ResolveInstrumentCurrencyForBuy();
        if (string.IsNullOrWhiteSpace(instrumentCurrency))
            return false;

        // A typed-but-not-yet-created account currently defaults to TWD in
        // TransactionDialogViewModel.ResolveCashAccountIdAsync, so treat an
        // unknown selected currency as TWD for the safeguard.
        var cashCurrency = string.IsNullOrWhiteSpace(BuyContext.CashAccountCurrency)
            ? "TWD"
            : BuyContext.CashAccountCurrency.Trim();

        return !string.Equals(instrumentCurrency, cashCurrency, StringComparison.OrdinalIgnoreCase);
    }

    private string ResolveInstrumentCurrencyForBuy()
    {
        if (!string.IsNullOrWhiteSpace(AddSymbolCurrency))
            return AddSymbolCurrency.Trim().ToUpperInvariant();

        // BuyContext.InstrumentCurrency is written from the selected asset/symbol,
        // not from the funding account currency. Keep it below AddSymbolCurrency
        // so symbol-directory data remains the strongest source.
        if (!string.IsNullOrWhiteSpace(BuyContext.InstrumentCurrency))
            return BuyContext.InstrumentCurrency.Trim().ToUpperInvariant();

        var exchange = EmptyToNull(AddExchange);
        if (string.IsNullOrWhiteSpace(exchange))
        {
            var typedSymbol = AddSymbol.Trim();
            var exactMatch = _addAssetWorkflow.SearchSymbols(typedSymbol, 8)
                .FirstOrDefault(s => string.Equals(s.Symbol, typedSymbol, StringComparison.OrdinalIgnoreCase));
            if (exactMatch is not null)
            {
                if (!string.IsNullOrWhiteSpace(exactMatch.Currency))
                    return exactMatch.Currency.Trim().ToUpperInvariant();

                exchange = exactMatch.Exchange;
            }
        }

        return string.IsNullOrWhiteSpace(exchange)
            ? "TWD"
            : StockExchangeRegistry.ResolveDefaultCurrency(exchange);
    }

    // ── Crypto ────────────────────────────────────────────────────────────────────────

    private async Task AddCryptoAsync()
    {
        AddError = string.Empty;
        var sym = AddCryptoSymbol.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(sym))
        { AddError = "請輸入幣種代號（如 BTC）"; return; }
        if (!ParseHelpers.TryParseDecimal(AddCryptoQty, out var qty) || qty <= 0)
        { AddError = "數量無效"; return; }
        if (!ParseHelpers.TryParseDecimal(AddCryptoPrice, out var price) || price <= 0)
        { AddError = "單價無效"; return; }

        var cryptoBuyDate = DateOnly.FromDateTime(AddBuyDate.Date);
        await _addAssetWorkflow.CreateManualAssetAsync(new ManualAssetCreateRequest(
            sym,
            string.Empty,
            sym,
            AssetType.Crypto,
            qty,
            price * qty,
            price,
            cryptoBuyDate,
            SelectedPortfolioGroupId));

        AddCryptoSymbol = string.Empty;
        AddCryptoQty = string.Empty;
        AddCryptoPrice = string.Empty;
        IsAddDialogOpen = false;

        AssetAdded?.Invoke(this, EventArgs.Empty);
    }

    // ── Fund / precious-metal / bond ─────────────────────────────────────────────────

    private async Task AddNonStockAsync(AssetType assetType)
    {
        AddError = string.Empty;
        if (string.IsNullOrWhiteSpace(AddName))
        { AddError = "請輸入名稱"; return; }
        if (!ParseHelpers.TryParseDecimal(AddCost, out var cost) || cost <= 0)
        { AddError = "持有成本無效"; return; }

        var nonStockDate = DateOnly.FromDateTime(AddBuyDate.Date);
        await _addAssetWorkflow.CreateManualAssetAsync(new ManualAssetCreateRequest(
            AddName.Trim(),
            string.Empty,
            AddName.Trim(),
            assetType,
            1m,
            cost,
            cost,
            nonStockDate,
            SelectedPortfolioGroupId));

        AddName = string.Empty;
        AddCost = string.Empty;
        IsAddDialogOpen = false;

        AssetAdded?.Invoke(this, EventArgs.Empty);
    }

    // ── Cash account ─────────────────────────────────────────────────────────────────

    private async Task AddAccountAsync()
    {
        AddError = string.Empty;
        if (string.IsNullOrWhiteSpace(AddAccountName))
        { AddError = "請輸入帳戶名稱"; return; }

        // Balance is a projection over the trade journal — a fresh account starts at 0
        // and only gains value once the user records a Deposit / Income / etc. trade.
        var created = await _accountUpsertWorkflow.CreateAsync(new CreateAccountRequest(
            AddAccountName.Trim(),
            // User's selected currency (was hard-coded "TWD"). Falls back to
            // "TWD" if the picker default was somehow cleared.
            string.IsNullOrWhiteSpace(AddAccountCurrency) ? "TWD" : AddAccountCurrency,
            DateOnly.FromDateTime(DateTime.Today),
            Subtype: string.IsNullOrWhiteSpace(AddSubtype) ? null : AddSubtype.Trim()));

        if (AddInitialDepositEnabled)
        {
            if (_transactionWorkflow is null)
            { AddError = "初始存入功能尚未就緒"; return; }
            if (!ParseHelpers.TryParseDecimal(AddInitialDepositAmount, out var initialAmount) || initialAmount <= 0)
            { AddError = "初始存入金額無效"; return; }

            await _transactionWorkflow.RecordCashFlowAsync(new CashFlowTransactionRequest(
                TradeType.Deposit,
                initialAmount,
                (AddInitialDepositDate ?? DateTime.Today).Date,
                created.Account.Id,
                created.Account.Name,
                string.IsNullOrWhiteSpace(AddInitialDepositNote) ? null : AddInitialDepositNote.Trim(),
                0m));
        }

        AddAccountName = string.Empty;
        AddAccountCurrency = "TWD";  // reset to fallback; reopen will refresh from Settings
        AddInitialDepositEnabled = false;
        AddInitialDepositAmount = string.Empty;
        AddInitialDepositDate = DateTime.Today;
        AddInitialDepositNote = string.Empty;

        IsAddDialogOpen = false;

        AssetAdded?.Invoke(this, EventArgs.Empty);
    }

    private async Task AddCreditCardAsync()
    {
        AddError = string.Empty;
        if (string.IsNullOrWhiteSpace(AddCreditCardName))
        { AddError = "請輸入信用卡名稱"; return; }

        int? billingDay = null;
        if (!string.IsNullOrWhiteSpace(AddCreditCardBillingDay))
        {
            if (!ParseHelpers.TryParseInt(AddCreditCardBillingDay, out var parsedBillingDay) || parsedBillingDay is < 1 or > 31)
            { AddError = "帳單日需介於 1 到 31"; return; }
            billingDay = parsedBillingDay;
        }

        int? dueDay = null;
        if (!string.IsNullOrWhiteSpace(AddCreditCardDueDay))
        {
            if (!ParseHelpers.TryParseInt(AddCreditCardDueDay, out var parsedDueDay) || parsedDueDay is < 1 or > 31)
            { AddError = "繳款截止日需介於 1 到 31"; return; }
            dueDay = parsedDueDay;
        }

        decimal? creditLimit = null;
        if (!string.IsNullOrWhiteSpace(AddCreditCardLimit))
        {
            if (!ParseHelpers.TryParseDecimal(AddCreditCardLimit, out var parsedLimit) || parsedLimit <= 0)
            { AddError = "信用額度無效"; return; }
            creditLimit = parsedLimit;
        }

        var created = await _creditCardMutationWorkflow.CreateAsync(new CreateCreditCardRequest(
            AddCreditCardName.Trim(),
            "TWD",
            DateOnly.FromDateTime(DateTime.Today),
            billingDay,
            dueDay,
            creditLimit,
            string.IsNullOrWhiteSpace(AddCreditCardIssuer) ? null : AddCreditCardIssuer.Trim(),
            string.IsNullOrWhiteSpace(AddSubtype) ? null : AddSubtype.Trim()));

        if (AddInitialCreditCardBalanceEnabled)
        {
            if (_creditCardTransactionWorkflow is null)
            { AddError = "初始未繳金額功能尚未就緒"; return; }
            if (!ParseHelpers.TryParseDecimal(AddInitialCreditCardBalanceAmount, out var initialAmount) || initialAmount <= 0)
            { AddError = "目前未繳金額無效"; return; }

            await _creditCardTransactionWorkflow.ChargeAsync(new CreditCardChargeRequest(
                created.CreditCard.Id,
                created.CreditCard.Name,
                (AddInitialCreditCardBalanceDate ?? DateTime.Today).Date,
                initialAmount,
                string.IsNullOrWhiteSpace(AddInitialCreditCardBalanceNote) ? null : AddInitialCreditCardBalanceNote.Trim()));
        }

        AddSubtype = string.Empty;
        AddCreditCardName = string.Empty;
        AddCreditCardIssuer = string.Empty;
        AddCreditCardBillingDay = string.Empty;
        AddCreditCardDueDay = string.Empty;
        AddCreditCardLimit = string.Empty;
        AddInitialCreditCardBalanceEnabled = false;
        AddInitialCreditCardBalanceAmount = string.Empty;
        AddInitialCreditCardBalanceDate = DateTime.Today;
        AddInitialCreditCardBalanceNote = string.Empty;
        IsAddDialogOpen = false;

        AssetAdded?.Invoke(this, EventArgs.Empty);
    }

    private async Task AddLoanAsync()
    {
        AddError = string.Empty;
        if (_loanMutationWorkflow is null)
        {
            AddError = "貸款功能尚未就緒";
            return;
        }
        if (string.IsNullOrWhiteSpace(AddLoanName))
        { AddError = "請輸入貸款名稱"; return; }
        if (!ParseHelpers.TryParseDecimal(AddLoanAmount, out var amount) || amount <= 0)
        { AddError = "借款金額無效"; return; }
        if (!ParseHelpers.TryParseDecimal(AddLoanAnnualRate, out var annualRate) || annualRate <= 0)
        { AddError = "年利率無效"; return; }
        if (!ParseHelpers.TryParseInt(AddLoanTermMonths, out var termMonths) || termMonths <= 0)
        { AddError = "還款期數無效"; return; }

        decimal fee = 0m;
        if (!string.IsNullOrWhiteSpace(AddLoanHandlingFee))
        {
            if (!ParseHelpers.TryParseDecimal(AddLoanHandlingFee, out fee) || fee < 0)
            { AddError = "手續費無效"; return; }
        }

        var loanDate = (AddLoanStartDate ?? DateTime.Today).Date;
        var firstPaymentDate = DateOnly.FromDateTime(loanDate);
        await _loanMutationWorkflow.RecordAsync(new LoanTransactionRequest(
            TradeType.LoanBorrow,
            amount,
            loanDate,
            AddLoanName.Trim(),
            SelectedLoanCashAccount?.Id,
            null,
            fee,
            AmortAnnualRate: annualRate / 100m,
            AmortTermMonths: termMonths,
            FirstPaymentDate: firstPaymentDate,
            Subtype: string.IsNullOrWhiteSpace(AddSubtype) ? null : AddSubtype.Trim()));

        AddSubtype = string.Empty;
        AddLoanName = string.Empty;
        AddLoanAmount = string.Empty;
        AddLoanAnnualRate = string.Empty;
        AddLoanTermMonths = string.Empty;
        AddLoanHandlingFee = string.Empty;
        AddLoanStartDate = DateTime.Today;
        SelectedLoanCashAccount = null;
        IsAddDialogOpen = false;

        AssetAdded?.Invoke(this, EventArgs.Empty);
    }

    // ── Dispose ───────────────────────────────────────────────────────────────────────

    public void CancelPendingFetch()
    {
        _closePriceCts?.Cancel();
        _closePriceCts?.Dispose();
        _closePriceCts = null;
    }

    // ── Private validation helpers ────────────────────────────────────────────────────

    private static string ValidatePositiveDecimalOrEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty :
        !ParseHelpers.TryParseDecimal(value, out var v) || v <= 0 ? "請輸入大於 0 的數字" : string.Empty;

    private static bool TryResolveManualFee(string? value, out decimal? manualFee)
    {
        manualFee = null;
        if (string.IsNullOrWhiteSpace(value))
            return true;

        if (!ParseHelpers.TryParseDecimal(value, out var parsed) || parsed < 0)
            return false;

        manualFee = parsed;
        return true;
    }

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

    /// <summary>
    /// M3 — owns its own field reset for account-mode open. Previously
    /// PortfolioViewModel.OpenAddAccountDialog poked ~10 individual
    /// AddAssetDialog properties from outside, drifting out of sync as
    /// fields were added. Keeps reset logic next to the fields it touches.
    /// </summary>
    public void ResetForAccountForm()
    {
        AddDialogMode = "account";
        IsTypePickerStep = true;
        AddError = string.Empty;
        AddSubtype = string.Empty;
        AddAccountName = string.Empty;
        // Default to the user's primary currency (set in Settings) so a JPY
        // user doesn't have to flip the picker for every account. Falls back
        // to "TWD" when no provider is wired (test fixtures, etc.).
        AddAccountCurrency = ResolveDefaultCurrency();
        AddInitialDepositEnabled = false;
        AddInitialDepositAmount = string.Empty;
        AddInitialDepositDate = DateTime.Today;
        AddInitialDepositNote = string.Empty;
        IsAddDialogOpen = true;
    }

    /// <summary>
    /// Returns the user's preferred default currency for new accounts. Wired
    /// by the parent VM via <see cref="GetDefaultCurrency"/>; defaults to "TWD"
    /// when not provided.
    /// </summary>
    public Func<string> GetDefaultCurrency { get; set; } = () => "TWD";

    private string ResolveDefaultCurrency()
    {
        var c = GetDefaultCurrency();
        return string.IsNullOrWhiteSpace(c) ? "TWD" : c;
    }

    /// <summary>
    /// M3 — owns its own field reset for liability-mode open. Same
    /// rationale as <see cref="ResetForAccountForm" />: previously 16
    /// loan + credit-card fields cleared by the parent VM.
    /// </summary>
    public void ResetForLiabilityForm()
    {
        AddDialogMode = "liability";
        IsTypePickerStep = true;
        AddError = string.Empty;
        AddLoanName = string.Empty;
        AddLoanAmount = string.Empty;
        AddLoanAnnualRate = string.Empty;
        AddLoanTermMonths = string.Empty;
        AddLoanHandlingFee = string.Empty;
        AddLoanStartDate = DateTime.Today;
        SelectedLoanCashAccount = null;
        AddCreditCardName = string.Empty;
        AddCreditCardIssuer = string.Empty;
        AddCreditCardBillingDay = string.Empty;
        AddCreditCardDueDay = string.Empty;
        AddCreditCardLimit = string.Empty;
        AddInitialCreditCardBalanceEnabled = false;
        AddInitialCreditCardBalanceAmount = string.Empty;
        AddInitialCreditCardBalanceDate = DateTime.Today;
        AddInitialCreditCardBalanceNote = string.Empty;
        IsAddDialogOpen = true;
    }
}
