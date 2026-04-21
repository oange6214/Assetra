using System.Collections.ObjectModel;
using Assetra.Application.Portfolio.Contracts;
using Assetra.Core.Interfaces;
using Assetra.WPF.Infrastructure;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Assetra.WPF.Features.Portfolio.SubViewModels;

/// <summary>
/// Owns all sell-panel observable state and the CancelSell / ConfirmSell commands.
/// BeginSell / BeginSellForSelectedPosition remain on <see cref="PortfolioViewModel"/>
/// because they open the Tx dialog (which lives on the parent VM).
///
/// After a successful sell, <see cref="SellCompleted"/> is raised so the parent VM
/// can reload positions, trades, account balances, and totals.
/// </summary>
public partial class SellPanelViewModel : ObservableObject
{
    private readonly ISellWorkflowService _sellWorkflow;
    private readonly PortfolioSellPanelController _controller;
    private readonly ISnackbarService? _snackbar;
    private readonly ILocalizationService? _localization;

    /// <summary>
    /// Raised after a successful sell so the parent VM can trigger its reload pipeline.
    /// </summary>
    public event EventHandler? SellCompleted;

    // ── Delegates wired by PortfolioViewModel after construction ──────────────────────
    // These avoid a back-reference to PortfolioViewModel (circular coupling).

    /// <summary>Returns the current commission-discount value from the Tx dialog (0–1).</summary>
    public Func<decimal> GetTxCommissionDiscountValue { get; set; } = () => 1m;

    /// <summary>Returns the raw TxFee string from the Tx dialog.</summary>
    public Func<string> GetTxFee { get; set; } = () => string.Empty;

    /// <summary>Returns the current sell-quantity override (partial sell) from the Tx dialog.</summary>
    public Func<int> GetSellQtyOverride { get; set; } = () => 0;

    /// <summary>
    /// Cash account list for the sell-panel cash-account picker.
    /// Wired by the parent VM to its own <c>CashAccounts</c> collection.
    /// </summary>
    public ObservableCollection<CashAccountRowViewModel> CashAccounts { get; set; } = [];

    // ── Sell Panel state ──────────────────────────────────────────────────────────────

    [ObservableProperty] private PortfolioRowViewModel? _sellingRow;
    [ObservableProperty] private bool _isSellPanelVisible;
    [ObservableProperty] private string _sellPriceInput = string.Empty;
    [ObservableProperty] private string _sellPanelError = string.Empty;
    [ObservableProperty] private bool _isSellEtf;

    // Fee breakdown (live-updated as user types the sell price)
    [ObservableProperty] private decimal _sellGrossAmount;
    [ObservableProperty] private decimal _sellCommission;
    [ObservableProperty] private decimal _sellTransactionTax;
    [ObservableProperty] private decimal _sellNetAmount;
    [ObservableProperty] private decimal _sellEstimatedPnl;
    [ObservableProperty] private bool _isSellEstimatedPositive;

    // Cash account to credit after the sell (null = no cash linkage)
    [ObservableProperty] private CashAccountRowViewModel? _sellCashAccount;

    partial void OnSellPriceInputChanged(string value) => UpdateSellPreview();

    private void UpdateSellPreview()
    {
        var preview = _controller.BuildPreview(
            SellingRow,
            SellPriceInput,
            GetTxCommissionDiscountValue(),
            IsSellEtf);
        SellGrossAmount = preview.GrossAmount;
        SellCommission = preview.Commission;
        SellTransactionTax = preview.TransactionTax;
        SellNetAmount = preview.NetAmount;
        SellEstimatedPnl = preview.EstimatedPnl;
        IsSellEstimatedPositive = preview.IsEstimatedPositive;
    }

    // ── Constructor ───────────────────────────────────────────────────────────────────

    internal SellPanelViewModel(
        ISellWorkflowService sellWorkflow,
        PortfolioSellPanelController controller,
        ISnackbarService? snackbar = null,
        ILocalizationService? localization = null)
    {
        _sellWorkflow = sellWorkflow;
        _controller = controller;
        _snackbar = snackbar;
        _localization = localization;
    }

    // ── Commands ──────────────────────────────────────────────────────────────────────

    [RelayCommand]
    private void CancelSell()
    {
        SellingRow = null;
        IsSellPanelVisible = false;
        SellPriceInput = string.Empty;
        SellPanelError = string.Empty;
        IsSellEtf = false;
        SellGrossAmount = 0;
        SellCommission = 0;
        SellTransactionTax = 0;
        SellNetAmount = 0;
        SellEstimatedPnl = 0;
        IsSellEstimatedPositive = false;
        SellCashAccount = null;
    }

    /// <summary>
    /// Confirms a sell — validates sell price, records the trade, archives consumed lots.
    /// Operates on <see cref="SellingRow"/> set externally before calling this command.
    /// Supports partial sells: <see cref="GetSellQtyOverride"/> &gt; 0 overrides the default full-qty sell.
    /// Archived lots (not hard-deleted) can be restored if the sell trade is later deleted.
    /// </summary>
    [RelayCommand]
    internal async Task ConfirmSell()
    {
        if (SellingRow is null)
            return;
        var row = SellingRow;
        var submit = _controller.BuildSubmission(
            row,
            SellPriceInput,
            GetTxFee(),
            GetSellQtyOverride(),
            GetTxCommissionDiscountValue(),
            IsSellEtf,
            SellCashAccount?.Id);
        SellPanelError = submit.Error ?? string.Empty;
        if (submit.Request is null)
            return;

        try
        {
            await _sellWorkflow.RecordAsync(submit.Request)
                .ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Failed to record sell trade for {Symbol}", row.Symbol);
            _snackbar?.Warning(L("Portfolio.Sell.TradeSaveFailed", "賣出已完成，但交易記錄儲存失敗"));
            return;
        }

        CancelSell();
        SellCompleted?.Invoke(this, EventArgs.Empty);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Re-fires PropertyChanged for all currency-sensitive display properties so
    /// bound CurrencyConverter instances re-evaluate. Called by the parent VM when
    /// the active display currency changes.
    /// </summary>
    public void NotifyCurrencyChanged()
    {
        OnPropertyChanged(nameof(SellGrossAmount));
        OnPropertyChanged(nameof(SellCommission));
        OnPropertyChanged(nameof(SellTransactionTax));
        OnPropertyChanged(nameof(SellNetAmount));
        OnPropertyChanged(nameof(SellEstimatedPnl));
    }

    private string L(string key, string fallback = "") =>
        _localization?.Get(key, fallback) ?? fallback;
}
