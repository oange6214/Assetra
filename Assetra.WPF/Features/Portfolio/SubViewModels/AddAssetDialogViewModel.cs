using Assetra.Application.Portfolio.Contracts;
using Assetra.Application.Portfolio.Dtos;
using Assetra.Core.Models;
using Assetra.Core.Trading;
using Assetra.WPF.Infrastructure;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

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
        IAccountUpsertWorkflowService accountUpsertWorkflow)
    {
        _addAssetWorkflow = addAssetWorkflow;
        _accountUpsertWorkflow = accountUpsertWorkflow;
    }

    // ── Dialog visibility ────────────────────────────────────────────────────────────

    [ObservableProperty] private bool _isAddDialogOpen;
    [ObservableProperty] private bool _addDialogIsInvestmentMode = true;

    // ── Asset type ───────────────────────────────────────────────────────────────────

    [ObservableProperty] private string _addAssetType = "stock";
    // "stock" | "fund" | "metal" | "bond" | "crypto" | "cash" | "liability"

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
        catch { ClosePriceHint = "收盤價查詢失敗，請手動輸入"; }
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

    // ── Asset type toggle helpers (for RadioButton IsChecked TwoWay) ─────────────────

    public bool AddTypeIsStock { get => AddAssetType == "stock"; set { if (value) AddAssetType = "stock"; } }
    public bool AddTypeIsFund { get => AddAssetType == "fund"; set { if (value) AddAssetType = "fund"; } }
    public bool AddTypeIsMetal { get => AddAssetType == "metal"; set { if (value) AddAssetType = "metal"; } }
    public bool AddTypeIsBond { get => AddAssetType == "bond"; set { if (value) AddAssetType = "bond"; } }
    public bool AddTypeIsCrypto { get => AddAssetType == "crypto"; set { if (value) AddAssetType = "crypto"; } }
    public bool AddTypeIsAccount { get => AddAssetType == "cash"; set { if (value) AddAssetType = "cash"; } }

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
        OnPropertyChanged(nameof(AddTypeIsNonStockInvestment));
        OnPropertyChanged(nameof(AddTypeIsCryptoForm));
        AddError = string.Empty;
    }

    // ── Commands ─────────────────────────────────────────────────────────────────────

    [RelayCommand]
    private void CloseAddDialog()
    {
        IsSuggestionsOpen = false;
        IsAddDialogOpen = false;
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
        var manualFee = !string.IsNullOrWhiteSpace(txFee) &&
                        ParseHelpers.TryParseDecimal(txFee, out var parsedManualFee) &&
                        parsedManualFee >= 0
            ? parsedManualFee
            : (decimal?)null;
        var cashAccId = GetTxUseCashAccount() ? GetTxCashAccountId() : null;
        await _addAssetWorkflow.ExecuteStockBuyAsync(new StockBuyRequest(
            symbol,
            price,
            qty,
            DateOnly.FromDateTime(AddBuyDate),
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

        var cryptoBuyDate = DateOnly.FromDateTime(DateTime.Today);
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

        var nonStockDate = DateOnly.FromDateTime(DateTime.Today);
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
        await _accountUpsertWorkflow.CreateAsync(new CreateAccountRequest(
            AddAccountName.Trim(),
            "TWD",
            DateOnly.FromDateTime(DateTime.Today)));

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
}
