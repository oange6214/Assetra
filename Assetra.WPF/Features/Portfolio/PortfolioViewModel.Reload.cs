using Assetra.Core.Dtos;
using Assetra.Core.Models;
using Serilog;

namespace Assetra.WPF.Features.Portfolio;

/// <summary>
/// PortfolioViewModel partial — sub-VM event handlers, reload-after-mutation orchestration,
/// quote-stream updates, snapshot maintenance, and currency-change propagation.
/// </summary>
public partial class PortfolioViewModel
{
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
        Financial.Apply(_summaryService.Calculate(BuildSummaryInput()));
    }
}
