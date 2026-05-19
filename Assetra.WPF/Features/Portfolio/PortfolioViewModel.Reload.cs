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
        // 帳戶 metadata 變更（如 Subtype 改變影響 GroupId）需要 FinancialOverview 重撈分組，
        // 但 LoadCashAccountsAsync / RebuildTotals 不會觸發 TotalMarketValue 通知。
        // 主動 raise TotalCash PropertyChanged（即使數值未變）讓 FinancialOverview 重新載入。
        OnPropertyChanged(nameof(TotalCash));
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
            var row = FindQuoteTarget(quote);
            if (row is null)
                continue;

            if (string.IsNullOrEmpty(row.Name) && !string.IsNullOrEmpty(quote.Name))
                row.Name = quote.Name;

            if (!string.IsNullOrWhiteSpace(quote.Currency))
                row.Currency = quote.Currency;

            row.CurrentPrice = quote.Price;
            row.PrevClose = quote.PrevClose;
            row.IsQuoteStale = quote.IsStale;
            row.QuoteProviderStateMessage = quote.ProviderStateMessage;
            row.IsLoadingPrice = false;
            row.Refresh();
            changed = true;
        }
        if (changed)
            RebuildTotals();
    }

    private PortfolioRowViewModel? FindQuoteTarget(StockQuote quote)
    {
        var exact = Positions.FirstOrDefault(p =>
            string.Equals(p.Symbol, quote.Symbol, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(p.Exchange, quote.Exchange, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
            return exact;

        // Legacy rows may not have Exchange populated. Only fall back to those rows;
        // never update a populated exchange with a quote from another exchange.
        var legacyMatches = Positions.Where(p =>
            string.Equals(p.Symbol, quote.Symbol, StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(p.Exchange)).Take(2).ToList();
        return legacyMatches.Count == 1 ? legacyMatches[0] : null;
    }

    private async Task RecordSnapshotAsync()
    {
        try
        {
            // Partial-price guard — 解決「資產趨勢出現一日跳水 + 隔日反彈」假象。
            //
            // 原本只有 TotalMarketValue > 0 + positionCount > 0 兩道閘，無法防止 app
            // 啟動時 quote API 還沒全部回來就觸發 snapshot 寫入：缺一檔股票的 quote
            // → 該 row CurrentPrice = 0 → MarketValue 缺值 → TotalMarketValue 短少。
            // INSERT OR REPLACE 寫進 DB 後，使用者切走頁面 / 關 app 前如果完整 quote
            // 還沒回來，當日 snapshot 就是這個缺值版本，造成 trend chart 一日跳水。
            //
            // Guard：任一檔有持倉的 equity / fund position 還是 CurrentPrice = 0 就跳
            // 過。Crypto / Bond 等可能合理為 0 的 type 不算（避免誤殺）。
            if (HasUnpricedPositions())
                return;

            var baseCcy = _settingsService?.Current?.BaseCurrency;
            var written = await _historyMaintenanceService.TryRecordSnapshotAsync(
                TotalCost, TotalMarketValue, TotalPnl, Positions.Count,
                string.IsNullOrWhiteSpace(baseCcy) ? "TWD" : baseCcy,
                // v0.30+ daily NW snapshot：把現金與負債一起記錄，讓未來
                // KPI bar 30 天淨值 sparkline 不再需要用 MarketValue 作 proxy。
                cashValue: TotalCash,
                liabilityValue: TotalLiabilities);
            if (written)
                await History.LoadAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[Portfolio] RecordSnapshotAsync failed");
        }
    }

    /// <summary>
    /// True 代表至少一檔「應該有報價」的持倉目前 CurrentPrice = 0，通常是 quote API
    /// 還沒回應完。回 true 時 caller 應該跳過寫 snapshot，等下一輪 totals 重算再試。
    /// </summary>
    private bool HasUnpricedPositions()
    {
        foreach (var p in Positions)
        {
            if (p.Quantity <= 0) continue;
            // 只認需要 live quote 才會有 price 的 type：Stock / Fund。
            // Crypto / Bond / PreciousMetal 等 type 即使 CurrentPrice=0 也可能是手動估值 fallback。
            if (p.AssetType is not (Core.Models.AssetType.Stock or Core.Models.AssetType.Fund))
                continue;
            if (p.CurrentPrice == 0m)
                return true;
        }
        return false;
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
        TradeFilter.NotifyCurrencyChanged();
        SellPanel.NotifyCurrencyChanged();
        RebuildTotals();
    }
}
