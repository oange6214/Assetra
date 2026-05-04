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
    // These are accessed as delegates / properties so the sub-VM does not hold
    // a back-reference to PortfolioViewModel (avoids circular coupling).

    /// <summary>Returns the current commission-discount value from the Tx dialog (0–1).</summary>
    public Func<decimal> GetTxCommissionDiscountValue { get; set; } = () => 1m;

    /// <summary>Returns the raw TxFee string from the Tx dialog.</summary>
    public Func<string> GetTxFee { get; set; } = () => string.Empty;

    /// <summary>Returns whether TxBuyMetaOnly is set in the Tx dialog.</summary>
    public Func<bool> GetTxBuyMetaOnly { get; set; } = () => false;

    /// <summary>Returns the cash account ID to link to the buy (null = none).</summary>
    public Func<Guid?> GetTxCashAccountId { get; set; } = () => null;

    /// <summary>Returns whether to use a cash account for the buy.</summary>
    public Func<bool> GetTxUseCashAccount { get; set; } = () => false;

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

    private async Task FetchClosePriceAsync(string symbol, DateTime buyDate, CancellationToken ct)
    {
        try
        {
            if (SuppressClosePriceAutoFill)
                return;
            IsLoadingClosePrice = true;
            ClosePriceHint = string.Empty;
            var result = await _addAssetWorkflow.LookupClosePriceAsync(symbol, buyDate, ct);
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

        var txFee = GetTxFee();
        var preview = _addAssetWorkflow.BuildBuyPreview(new BuyPreviewRequest(
            AddSymbol.Trim(),
            price,
            qty,
            GetTxCommissionDiscountValue(),
            !string.IsNullOrWhiteSpace(txFee) &&
            ParseHelpers.TryParseDecimal(txFee, out var manualFee) && manualFee >= 0
                ? manualFee
                : null));

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
        // INVARIANT (Task 18 Option B): when TxBuyMetaOnly is true we MUST NOT write a
        // Trade row. Callers rely on this to pre-register symbols without affecting P&L.
        if (GetTxBuyMetaOnly())
        {
            AddError = string.Empty;
            if (string.IsNullOrWhiteSpace(AddSymbol))
            { AddError = "請輸入股票代號"; return; }

            var metaSymbol = AddSymbol.Trim().ToUpper();
            await _addAssetWorkflow
                .EnsureStockEntryAsync(new EnsureStockEntryRequest(metaSymbol))
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
        if (!ParseHelpers.TryParseDecimal(AddPrice, out var price) || price <= 0)
        { AddError = "成交價無效"; return; }

        var symbol = AddSymbol.Trim().ToUpper();
        var txFee = GetTxFee();
        if (!TryResolveManualFee(txFee, out var manualFee))
        { AddError = "手續費無效"; return; }

        var cashAccId = GetTxUseCashAccount() ? GetTxCashAccountId() : null;
        await _addAssetWorkflow.ExecuteStockBuyAsync(new StockBuyRequest(
            symbol,
            price,
            qty,
            AddBuyDate,
            cashAccId,
            GetTxCommissionDiscountValue(),
            manualFee));

        // Cash balance reflects the Buy trade written above via the projection service;
        // no manual AdjustCashAccountAsync needed (single-truth architecture).

        AddSymbol = string.Empty;
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
            cryptoBuyDate));

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
            nonStockDate));

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
            "TWD",
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
        AddInitialDepositEnabled = false;
        AddInitialDepositAmount = string.Empty;
        AddInitialDepositDate = DateTime.Today;
        AddInitialDepositNote = string.Empty;
        IsAddDialogOpen = true;
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
