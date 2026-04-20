using System.Collections.ObjectModel;
using System.Reactive.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Assetra.WPF.Infrastructure;
using Assetra.Core.Trading;

namespace Assetra.WPF.Features.Portfolio;

public sealed record CurrencyOption(string Code, string Display);

/// <summary>
/// Add-asset dialog state, symbol suggestions, close-price fetching,
/// buy preview, all Add*Async methods, cash/liability CRUD,
/// SavePosition, sell panel, and GlobalAdd command.
/// </summary>
public partial class PortfolioViewModel
{
    // 新增資產 Dialog
    [ObservableProperty] private bool _isAddDialogOpen;
    [ObservableProperty] private bool _addDialogIsInvestmentMode = true;
    [ObservableProperty] private string _addAssetType = "stock";
    // "stock" | "fund" | "metal" | "bond" | "crypto" | "cash" | "liability"

    // Stock/ETF 欄位
    [ObservableProperty] private DateTime _addBuyDate = DateTime.Today;
    [ObservableProperty] private string _addSymbol = string.Empty;
    [ObservableProperty] private string _addPrice = string.Empty;
    [ObservableProperty] private string _addQuantity = string.Empty;
    [ObservableProperty] private string _addError = string.Empty;

    // 收盤價自動帶入狀態
    [ObservableProperty] private bool _isLoadingClosePrice;
    [ObservableProperty] private string _closePriceHint = string.Empty;

    // 買入費用即時試算
    [ObservableProperty] private decimal _addGrossAmount;
    [ObservableProperty] private decimal _addCommission;
    [ObservableProperty] private decimal _addTotalCost;
    [ObservableProperty] private decimal _addCostPerShare;

    public bool HasAddPreview => AddTotalCost > 0;
    partial void OnAddTotalCostChanged(decimal _) => OnPropertyChanged(nameof(HasAddPreview));

    // 代號自動完成
    [ObservableProperty] private bool _isSuggestionsOpen;
    [ObservableProperty] private StockSearchResult? _selectedSuggestion;
    public ObservableCollection<StockSearchResult> SymbolSuggestions { get; } = [];

    partial void OnSelectedSuggestionChanged(StockSearchResult? value)
    {
        if (value is null)
            return;
        SelectSuggestion(value);
        SelectedSuggestion = null;
    }
    private bool _suppressSuggestions;

    partial void OnAddSymbolChanged(string value)
    {
        if (_suppressSuggestions)
            return;
        if (!AddTypeIsStock || string.IsNullOrWhiteSpace(value))
        {
            IsSuggestionsOpen = false;
            SymbolSuggestions.Clear();
            ClosePriceHint = string.Empty;
            return;
        }
        var results = _search.Search(value.Trim());
        SymbolSuggestions.Clear();
        foreach (var r in (results ?? []).Take(8))
            SymbolSuggestions.Add(r);
        IsSuggestionsOpen = SymbolSuggestions.Count > 0;
        TriggerClosePriceFetch();
    }

    partial void OnAddBuyDateChanged(DateTime _) => TriggerClosePriceFetch();

    private void SelectSuggestion(StockSearchResult suggestion)
    {
        _suppressSuggestions = true;
        IsSuggestionsOpen = false;
        AddSymbol = suggestion.Symbol;
        _suppressSuggestions = false;
        TriggerClosePriceFetch();
    }

    // 歷史收盤價自動帶入
    private CancellationTokenSource? _closePriceCts;

    private void TriggerClosePriceFetch()
    {
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
        if (_historyProvider is null)
            return;
        IsLoadingClosePrice = true;
        ClosePriceHint = string.Empty;
        try
        {
            var targetDate = DateOnly.FromDateTime(buyDate);  // for history lookup (stays DateOnly)
            var exchange = _search.GetExchange(symbol) ?? InferExchange(symbol);
            var daysDiff = (DateTime.Today - buyDate).Days;
            var period = daysDiff <= 35 ? ChartPeriod.OneMonth
                           : daysDiff <= 100 ? ChartPeriod.ThreeMonths
                           : daysDiff <= 370 ? ChartPeriod.OneYear
                           : ChartPeriod.TwoYears;

            var history = await _historyProvider.GetHistoryAsync(symbol, exchange, period, ct);
            if (ct.IsCancellationRequested)
                return;

            // 找對應日期；若選到非交易日則找最近一個前交易日
            var point = history
                .Where(h => h.Date <= targetDate)
                .OrderByDescending(h => h.Date)
                .FirstOrDefault();

            if (point is not null)
            {
                AddPrice = point.Close.ToString("0.##");
                ClosePriceHint = point.Date == targetDate
                    ? $"已帶入 {targetDate:yyyy/MM/dd} 收盤價"
                    : $"已帶入最近交易日 {point.Date:yyyy/MM/dd} 收盤價";
            }
            else
            {
                ClosePriceHint = "查無收盤資料，請手動輸入";
            }
        }
        catch (OperationCanceledException) { /* 正常取消 */ }
        catch { ClosePriceHint = "收盤價查詢失敗，請手動輸入"; }
        finally { IsLoadingClosePrice = false; }
    }

    // 買入費用即時試算
    partial void OnAddPriceChanged(string _) => UpdateBuyPreview();
    partial void OnAddQuantityChanged(string _) => UpdateBuyPreview();

    private void UpdateBuyPreview()
    {
        if (!ParseHelpers.TryParseDecimal(AddPrice, out var price) || price <= 0 ||
            !int.TryParse(AddQuantity, out var qty) || qty <= 0)
        {
            AddGrossAmount = 0;
            AddCommission = 0;
            AddTotalCost = 0;
            AddCostPerShare = 0;
            return;
        }

        var gross = price * qty;
        decimal commission;

        // Manual fee override (TxFee field) — if filled, use that value verbatim.
        // Must match the logic in AddPosition() so preview and actual write are consistent.
        if (!string.IsNullOrWhiteSpace(TxFee) &&
            ParseHelpers.TryParseDecimal(TxFee, out var manualFee) && manualFee >= 0)
        {
            commission = manualFee;
        }
        else
        {
            var discount = TxCommissionDiscountValue;
            var isEtf = _search.IsEtf(AddSymbol.Trim());
            commission = TaiwanTradeFeeCalculator.CalcBuy(price, qty, discount, isEtf).Commission;
        }

        AddGrossAmount = gross;
        AddCommission = commission;
        AddTotalCost = gross + commission;
        AddCostPerShare = qty > 0 ? (gross + commission) / qty : 0m;
    }

    // 非股票投資欄位（基金 / 貴金屬 / 債券 / 加密貨幣）
    [ObservableProperty] private string _addName = string.Empty;
    [ObservableProperty] private string _addCost = string.Empty;

    // 加密貨幣欄位
    [ObservableProperty] private string _addCryptoSymbol = string.Empty;
    [ObservableProperty] private string _addCryptoQty = string.Empty;
    [ObservableProperty] private string _addCryptoPrice = string.Empty;

    // 現金帳戶欄位
    // 現金欄位 — 極簡：只收名稱，餘額從「存入/提款」交易累積
    [ObservableProperty] private string _addAccountName = string.Empty;

    // 類型切換輔助（供 RadioButton IsChecked TwoWay 使用）
    public bool AddTypeIsStock { get => AddAssetType == "stock"; set { if (value) AddAssetType = "stock"; } }
    public bool AddTypeIsFund { get => AddAssetType == "fund"; set { if (value) AddAssetType = "fund"; } }
    public bool AddTypeIsMetal { get => AddAssetType == "metal"; set { if (value) AddAssetType = "metal"; } }
    public bool AddTypeIsBond { get => AddAssetType == "bond"; set { if (value) AddAssetType = "bond"; } }
    public bool AddTypeIsCrypto { get => AddAssetType == "crypto"; set { if (value) AddAssetType = "crypto"; } }
    public bool AddTypeIsAccount { get => AddAssetType == "cash"; set { if (value) AddAssetType = "cash"; } }

    // 基金/貴金屬/債券共用「名稱 + 總成本」表單
    public bool AddTypeIsNonStockInvestment => AddAssetType is "fund" or "metal" or "bond";

    // 加密貨幣專屬表單（代號 + 數量 + 單價）
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

    // Sell Panel
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

    partial void OnSellPriceInputChanged(string value) => UpdateSellPreview();

    private void UpdateSellPreview()
    {
        if (SellingRow is null || !ParseHelpers.TryParseDecimal(SellPriceInput, out var price) || price <= 0)
        {
            SellGrossAmount = 0;
            SellCommission = 0;
            SellTransactionTax = 0;
            SellNetAmount = 0;
            SellEstimatedPnl = 0;
            IsSellEstimatedPositive = false;
            return;
        }
        var discount = TxCommissionDiscountValue;
        var fee = TaiwanTradeFeeCalculator.CalcSell(price, (int)SellingRow.Quantity, discount, IsSellEtf, SellingRow.IsBondEtf);
        SellGrossAmount = fee.GrossAmount;
        SellCommission = fee.Commission;
        SellTransactionTax = fee.TransactionTax;
        SellNetAmount = fee.NetAmount;
        SellEstimatedPnl = fee.NetAmount - SellingRow.BuyPrice * SellingRow.Quantity;
        IsSellEstimatedPositive = SellEstimatedPnl >= 0;
    }

    // Sell Panel commands

    [RelayCommand]
    private void BeginSell(PortfolioRowViewModel row)
    {
        // Open Tx dialog in Sell mode with this position pre-selected
        OpenTxDialog();
        TxType = "sell";
        TxSellPosition = row;
    }

    /// <summary>側面板「買入」快速動作 — 打開 Tx 對話框，預填當前股票代號。</summary>
    [RelayCommand]
    private void BeginBuyForSelectedPosition()
    {
        if (SelectedPositionRow is null)
            return;
        var row = SelectedPositionRow;
        OpenTxDialog();
        TxType = "buy";
        TxBuyAssetType = "stock";
        AddSymbol = row.Symbol;
        AddPrice = string.Empty;
        AddQuantity = string.Empty;
    }

    /// <summary>側面板「配息入帳」快速動作 — 打開 Tx 對話框並預選此持倉。</summary>
    [RelayCommand]
    private void BeginDividendForSelectedPosition()
    {
        if (SelectedPositionRow is null)
            return;
        OpenTxDialog();
        TxType = "cashDiv";
        TxDivPosition = SelectedPositionRow;
    }

    /// <summary>側面板「賣出」快速動作 — 呼叫既有 BeginSell，但以 SelectedPositionRow 為目標。</summary>
    [RelayCommand]
    private void BeginSellForSelectedPosition()
    {
        if (SelectedPositionRow is null)
            return;
        BeginSell(SelectedPositionRow);
    }

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
    /// Confirms a sell — validates sell price, records the trade, removes the position.
    /// Operates on <see cref="SellingRow"/> set by <see cref="BeginSellCommand"/>.
    /// </summary>
    [RelayCommand]
    private async Task ConfirmSell()
    {
        if (SellingRow is null)
            return;
        var row = SellingRow;
        SellPanelError = string.Empty;

        if (!ParseHelpers.TryParseDecimal(SellPriceInput, out var sellPrice) || sellPrice <= 0)
        {
            SellPanelError = "賣出價格無效";
            return;
        }

        // Sell-side commission + tax: user can override via TxFee (manual). Empty falls
        // back to auto-compute via TaiwanTradeFeeCalculator (commission × discount + tax).
        decimal sellNetAmount;    // proceeds after fees & tax
        decimal sellCommission;   // commission + transaction tax (stored with trade for history)
        decimal? sellDiscount;    // null = 手動覆蓋；有值 = 透過折扣計算（供編輯時還原）
        if (!string.IsNullOrWhiteSpace(TxFee) && ParseHelpers.TryParseDecimal(TxFee, out var manualFee) && manualFee >= 0)
        {
            sellNetAmount = sellPrice * row.Quantity - manualFee;
            sellCommission = manualFee;
            sellDiscount = null;
        }
        else
        {
            var discount = TxCommissionDiscountValue;
            var feeResult = TaiwanTradeFeeCalculator.CalcSell(sellPrice, (int)row.Quantity, discount, IsSellEtf, row.IsBondEtf);
            sellNetAmount  = feeResult.NetAmount;
            sellCommission = feeResult.Commission + feeResult.TransactionTax;
            sellDiscount = discount;
        }
        // Projection-driven P&L — queries the trade log (proportional COGS) instead of
        // relying on display-layer avg cost. Passes sellCommission (already combines
        // commission + tax in the manual-fee branch and auto-compute branch above).
        var realizedPnl = await _positionQuery
            .ComputeRealizedPnlAsync(row.Id, DateTime.UtcNow, sellPrice, row.Quantity, sellCommission)
            .ConfigureAwait(true);
        var buyCost = row.BuyPrice * row.Quantity;
        var realizedPnlPct = buyCost > 0
            ? realizedPnl / buyCost * 100m
            : 0m;

        var trade = new Trade(
            Id: Guid.NewGuid(),
            Symbol: row.Symbol,
            Exchange: row.Exchange,
            Name: row.Name,
            Type: TradeType.Sell,
            TradeDate: DateTime.UtcNow,
            Price: sellPrice,
            Quantity: (int)row.Quantity,
            RealizedPnl: realizedPnl,
            RealizedPnlPct: realizedPnlPct,
            CashAccountId: SellCashAccount?.Id,
            PortfolioEntryId: row.Id,
            Commission: sellCommission,
            CommissionDiscount: sellDiscount);

        try
        { await _tradeRepo.AddAsync(trade); }
        catch (Exception ex)
        {
            // 賣出操作本身繼續執行；交易記錄寫入失敗只記錄警告
            Serilog.Log.Warning(ex, "Failed to record sell trade for {Symbol}", row.Symbol);
            _snackbar?.Warning(Application.Current?.TryFindResource("Portfolio.Sell.TradeSaveFailed") as string
                ?? "賣出已完成，但交易記錄儲存失敗");
        }

        // Log removal (qty = 0) then remove all underlying lots from DB
        await WriteLogAsync(row.Id, row.Symbol, row.Exchange, 0, row.BuyPrice);

        foreach (var id in row.AllEntryIds)
            await _repo.RemoveAsync(id);
        Positions.Remove(row);
        HasNoPositions = Positions.Count == 0;

        // Cash balance is now a pure projection over trades; recording the Sell trade
        // above is the single source of truth. No AdjustCashAccountAsync needed.
        CancelSell();   // close sell panel (also clears SellCashAccount)
        await LoadTradesAsync();
        await ReloadAccountBalancesAsync();
        RebuildTotals();
    }

    // 全域「新增交易」按鈕 — 一律開啟交易對話框
    // 建立帳戶的動作改由各 tab 內的「新增帳戶」ghost 按鈕處理，讓主要動作
    // （買/賣/存/提/借/還/股利…）統一從一個入口進入。

    [RelayCommand]
    private void GlobalAdd() => OpenTxDialog();

    /// <summary>開啟新增現金帳戶對話框（由現金 tab 的「新增帳戶」按鈕呼叫）。</summary>
    [RelayCommand]
    private void OpenAddAccountDialog()
    {
        SelectedTab = PortfolioTab.Accounts;
        AddAssetType = "cash";
        AddError = string.Empty;
        AddAccountName = string.Empty;
        IsAddDialogOpen = true;
    }

    // 新增帳戶 Dialog commands

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

    // Stock / ETF

    [RelayCommand]
    private async Task AddPosition()
    {
        // INVARIANT (Task 18 Option B): when TxBuyMetaOnly is true we MUST NOT write a
        // Trade row. Callers rely on this to pre-register symbols without affecting P&L.
        if (TxBuyMetaOnly)
        {
            AddError = string.Empty;
            if (string.IsNullOrWhiteSpace(AddSymbol))
            { AddError = "請輸入股票代號"; return; }

            var metaSymbol = AddSymbol.Trim().ToUpper();
            var metaExchange = _search.GetExchange(metaSymbol) ?? InferExchange(metaSymbol);
            var metaName = _search.GetName(metaSymbol) ?? string.Empty;

            await _repo
                .FindOrCreatePortfolioEntryAsync(metaSymbol, metaExchange, metaName, AssetType.Stock)
                .ConfigureAwait(true);

            // No trade written. Clear input + refresh the Positions list via projection
            // so the new Qty=0 row appears (provided HideEmptyPositions is off).
            AddSymbol = string.Empty;
            AddPrice = string.Empty;
            AddQuantity = string.Empty;
            ClosePriceHint = string.Empty;
            IsAddDialogOpen = false;

            await LoadPositionsAsync();
            return;
        }

        AddError = string.Empty;
        if (string.IsNullOrWhiteSpace(AddSymbol))
        { AddError = "請輸入股票代號"; return; }
        if (!int.TryParse(AddQuantity, out var qty) || qty <= 0)
        { AddError = "股數無效"; return; }
        if (!ParseHelpers.TryParseDecimal(AddPrice, out var price) || price <= 0)
        { AddError = "成交價無效"; return; }

        var symbol = AddSymbol.Trim().ToUpper();
        var exchange = _search.GetExchange(symbol) ?? InferExchange(symbol);
        var name = _search.GetName(symbol) ?? string.Empty;

        // Buy-side commission: user can override via TxFee (manual). When TxFee is empty
        // we fall back to the auto-computed value × commission discount, matching the
        // Taiwan brokerage formula. Either way, the fee is folded into cost basis so P&L
        // accounts for both buy and sell sides.
        var isEtf = _search.IsEtf(symbol);
        decimal commission;
        // discountForRecord：走折扣路徑時存實際折扣值，走手動覆蓋時存 null；
        // 供編輯對話框還原 UI 狀態（看得出使用者當初是設定折扣或手動填費用）。
        decimal? discountForRecord;
        if (!string.IsNullOrWhiteSpace(TxFee) && ParseHelpers.TryParseDecimal(TxFee, out var manualFee) && manualFee >= 0)
        {
            commission = manualFee;
            discountForRecord = null;
        }
        else
        {
            var discount = TxCommissionDiscountValue;
            commission = TaiwanTradeFeeCalculator.CalcBuy(price, qty, discount, isEtf).NetAmount - price * qty;
            discountForRecord = discount;
        }
        var grossCost = price * qty + commission;
        var costPerShare = grossCost / qty;

        // Cash account linkage: respect the checkbox; even if TxCashAccount has a value,
        // ignore it when the checkbox is off (no cash effect).
        var cashAccId = TxUseCashAccount ? TxCashAccount?.Id : null;

        var buyDate = DateOnly.FromDateTime(AddBuyDate);
        // Lazy Upsert: reuse an existing PortfolioEntry for (symbol, exchange) if present,
        // else create a fresh one. Eliminates duplicate rows when the same symbol is bought
        // multiple times. `name` comes from the stock-search catalogue (may be "").
        var entryId = await _repo
            .FindOrCreatePortfolioEntryAsync(symbol, exchange, name, AssetType.Stock)
            .ConfigureAwait(true);
        var entry = new PortfolioEntry(entryId, symbol, exchange, AssetType.Stock, name);
        // (No AddAsync — FindOrCreate already persisted on the fresh-create path.)

        // Log the new position; trade price = actual stock price (without commission)
        await WriteLogAsync(entry.Id, symbol, exchange, qty, costPerShare);
        await WriteBuyTradeAsync(entry, name, buyDate, price, qty, cashAccId, commission, discountForRecord);

        // Cash balance reflects the Buy trade written above via the projection service;
        // no manual AdjustCashAccountAsync needed (single-truth architecture).

        var existing = Positions.FirstOrDefault(p => p.Symbol == symbol && p.AssetType == AssetType.Stock);
        if (existing is not null)
        {
            // Absorb the new lot into the existing aggregated row
            var totalCost = existing.BuyPrice * existing.Quantity + costPerShare * qty;
            var totalQty  = existing.Quantity + qty;
            existing.BuyPrice = totalQty > 0 ? totalCost / totalQty : existing.BuyPrice;
            existing.Quantity = totalQty;
            existing.AllEntryIds.Add(entry.Id);
            existing.Refresh();
        }
        else
        {
            // Build a local snapshot for the new entry (trade not persisted yet, so
            // project from the values we just computed).
            var localSnap = new PositionSnapshot(
                entry.Id, qty, costPerShare * qty, costPerShare, 0m, buyDate);
            var row = ToRow(entry, localSnap);
            Positions.Add(row);
            HasNoPositions = false;
        }
        RebuildTotals();

        AddSymbol = string.Empty;
        AddPrice = string.Empty;
        AddQuantity = string.Empty;
        AddGrossAmount = 0;
        AddCommission = 0;
        AddTotalCost = 0;
        ClosePriceHint = string.Empty;
        IsAddDialogOpen = false;

        await LoadTradesAsync();
        await ReloadAccountBalancesAsync();
        RebuildTotals();
    }

    private Task AddStockAsync() => AddPosition();

    /// <summary>
    /// Fallback when a symbol isn't in the local CSV index (e.g. newly listed securities).
    /// ETF bond-class suffixes (00981A, 00981B …) are listed on TPEX;
    /// everything else defaults to TWSE.
    /// </summary>
    private static string InferExchange(string symbol) =>
        System.Text.RegularExpressions.Regex.IsMatch(symbol, @"^\d{5}[A-Z]$")
            ? "TPEX"
            : "TWSE";

    // 加密貨幣

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
        var entry = new PortfolioEntry(
            Guid.NewGuid(), sym, string.Empty, AssetType.Crypto);
        await _repo.AddAsync(entry);

        var cryptoSnap = new PositionSnapshot(entry.Id, qty, price * qty, price, 0m, cryptoBuyDate);
        var row = ToRow(entry, cryptoSnap);
        Positions.Add(row);
        HasNoPositions = false;

        // Fetch live price immediately
        await RefreshCryptoPricesAsync();
        RebuildTotals();

        AddCryptoSymbol = string.Empty;
        AddCryptoQty = string.Empty;
        AddCryptoPrice = string.Empty;
        IsAddDialogOpen = false;
    }

    // 基金 / 貴金屬 / 債券

    private async Task AddNonStockAsync(AssetType assetType)
    {
        AddError = string.Empty;
        if (string.IsNullOrWhiteSpace(AddName))
        { AddError = "請輸入名稱"; return; }
        if (!ParseHelpers.TryParseDecimal(AddCost, out var cost) || cost <= 0)
        { AddError = "持有成本無效"; return; }

        var nonStockDate = DateOnly.FromDateTime(DateTime.Today);
        var entry = new PortfolioEntry(
            Guid.NewGuid(), AddName.Trim(), string.Empty, assetType);
        await _repo.AddAsync(entry);

        var nonStockSnap = new PositionSnapshot(entry.Id, 1m, cost, cost, 0m, nonStockDate);
        var row = ToRow(entry, nonStockSnap);
        Positions.Add(row);
        HasNoPositions = false;
        RebuildTotals();

        AddName = string.Empty;
        AddCost = string.Empty;
        IsAddDialogOpen = false;
    }

    // 現金帳戶

    private async Task AddAccountAsync()
    {
        AddError = string.Empty;
        if (string.IsNullOrWhiteSpace(AddAccountName))
        { AddError = "請輸入帳戶名稱"; return; }

        // Balance is a projection over the trade journal — a fresh account starts at 0
        // and only gains value once the user records a Deposit / Income / etc. trade.
        var account = new Core.Models.AssetItem(
            Guid.NewGuid(),
            AddAccountName.Trim(),
            Core.Models.FinancialType.Asset,
            null,
            "TWD",
            DateOnly.FromDateTime(DateTime.Today));

        if (_assetRepo is not null)
            await _assetRepo.AddItemAsync(account);

        CashAccounts.Add(new CashAccountRowViewModel(account, projectedBalance: 0m));
        HasNoCashAccounts = false;
        RebuildTotals();
        IsAddDialogOpen = false;
    }

    // Edit Asset dialog — shared across Cash, Liability, and Investment positions

    [ObservableProperty] private bool _isEditAssetDialogOpen;
    [ObservableProperty] private string _editAssetName = string.Empty;
    [ObservableProperty] private string _editAssetTypeLabel = string.Empty;
    [ObservableProperty] private string _editAssetError = string.Empty;
    [ObservableProperty] private string _editAssetSymbol = string.Empty;
    [ObservableProperty] private string _editAssetCurrency = "TWD";
    [ObservableProperty] private bool _editAssetIsStock;

    private string _editAssetKind = string.Empty;
    private Guid _editAssetId;
    private PortfolioRowViewModel? _editPositionRow;

    public static IReadOnlyList<CurrencyOption> SupportedCurrencies { get; } =
    [
        new("TWD", "TWD (NT$)"),
        new("USD", "USD ($)"),
        new("EUR", "EUR (€)"),
        new("JPY", "JPY (¥)"),
        new("GBP", "GBP (£)"),
        new("HKD", "HKD (HK$)"),
    ];

    [RelayCommand]
    private void OpenEditCash(CashAccountRowViewModel row)
    {
        _editAssetKind = "cash";
        _editAssetId = row.Id;
        EditAssetName = row.Name;
        EditAssetCurrency = row.Currency;
        EditAssetTypeLabel = Application.Current?.TryFindResource("Portfolio.Dialog.TypeCash") as string ?? "現金";
        EditAssetError = string.Empty;
        EditAssetIsStock = false;
        IsEditAssetDialogOpen = true;
    }

    [RelayCommand]
    private void OpenEditPosition(PortfolioRowViewModel row)
    {
        _editAssetKind = "position";
        _editPositionRow = row;
        EditAssetName = row.Name;
        EditAssetSymbol = row.Symbol;
        EditAssetCurrency = row.Currency;
        EditAssetTypeLabel = Application.Current?.TryFindResource("Portfolio.Dialog.Type" + row.AssetType) as string
                             ?? row.AssetType.ToString();
        EditAssetIsStock = row.IsStock;
        EditAssetError = string.Empty;
        IsEditAssetDialogOpen = true;
    }

    [RelayCommand]
    private void CloseEditAsset()
    {
        IsEditAssetDialogOpen = false;
        EditAssetError = string.Empty;
    }

    [RelayCommand]
    private async Task SaveEditAsset()
    {
        EditAssetError = string.Empty;
        if (string.IsNullOrWhiteSpace(EditAssetName))
        { EditAssetError = "名稱不得空白"; return; }

        var name = EditAssetName.Trim();

        var currency = EditAssetCurrency;

        if (_editAssetKind == "cash")
        {
            var row = CashAccounts.FirstOrDefault(r => r.Id == _editAssetId);
            if (row is not null)
            {
                var updated = new Core.Models.AssetItem(
                    row.Id, name, Core.Models.FinancialType.Asset, null, currency, row.CreatedDate);
                if (_assetRepo is not null)
                    await _assetRepo.UpdateItemAsync(updated);
                row.Name = name;
                row.Currency = currency;
            }
        }
        else if (_editAssetKind == "position" && _editPositionRow is { } posRow)
        {
            foreach (var id in posRow.AllEntryIds)
                await _repo.UpdateMetadataAsync(id, name, currency);
            posRow.Name = name;
            posRow.Currency = currency;
        }

        IsEditAssetDialogOpen = false;
    }

    [RelayCommand]
    private void ArchiveAccount(CashAccountRowViewModel? row)
    {
        if (row is null || _assetRepo is null) return;
        var msg = Application.Current?.TryFindResource("Portfolio.Confirm.ArchiveAccount") as string
                  ?? "確定封存此帳戶？（餘額保留；交易紀錄保留）";
        AskConfirm(msg, async () =>
        {
            await _assetRepo.ArchiveItemAsync(row.Id);
            await LoadCashAccountsAsync();
            if (DefaultCashAccountId == row.Id)
                await ApplyDefaultCashAccountAsync(null);
            RebuildTotals();
        });
    }

    /// <summary>
    /// Permanent hard-delete. Guarded: rejects if any trade row references this account.
    /// Plan Task 14 — message uses {0} count for clarity; navigation-to-trades shortcut
    /// is deferred (plan flags it as acceptable fallback).
    /// </summary>
    [RelayCommand]
    private void RemoveCashAccount(CashAccountRowViewModel? row)
    {
        if (row is null || _assetRepo is null) return;

        AskConfirm(
            Application.Current?.TryFindResource("Portfolio.Confirm.DeleteAccount") as string
                ?? "確定刪除此帳戶？",
            async () =>
            {
                var refs = await _assetRepo.HasTradeReferencesAsync(row.Id);
                if (refs > 0)
                {
                    var template = Application.Current?.TryFindResource("Portfolio.Account.HasReferencesError") as string
                                   ?? "尚有 {0} 筆交易引用此帳戶，請先處理";
                    var formatted = string.Format(System.Globalization.CultureInfo.CurrentCulture, template, refs);
                    _snackbar?.Warning(formatted);
                    return;
                }

                await _assetRepo.DeleteItemAsync(row.Id);
                CashAccounts.Remove(row);
                HasNoCashAccounts = CashAccounts.Count == 0;

                if (DefaultCashAccountId == row.Id)
                    await ApplyDefaultCashAccountAsync(null);

                RebuildTotals();
            });
    }

    /// <summary>目前設為預設的現金帳戶 Id（從 AppSettings 載入）。</summary>
    [ObservableProperty] private Guid? _defaultCashAccountId;

    /// <summary>將指定帳戶設為預設，並持久化到 AppSettings。</summary>
    [RelayCommand]
    private async Task SetAsDefaultCashAccount(CashAccountRowViewModel row)
    {
        if (row is null)
            return;
        await ApplyDefaultCashAccountAsync(row.Id);
    }

    /// <summary>清除目前的預設現金帳戶。</summary>
    [RelayCommand]
    private async Task ClearDefaultCashAccount()
    {
        await ApplyDefaultCashAccountAsync(null);
    }

    /// <summary>
    /// 切換指定帳戶的預設狀態：已是預設 → 清除；不是 → 設為預設。
    /// 供側面板那顆星形按鈕綁定，一顆按鈕做兩件事。
    /// </summary>
    [RelayCommand]
    private async Task ToggleDefaultCashAccount(CashAccountRowViewModel row)
    {
        if (row is null)
            return;
        await ApplyDefaultCashAccountAsync(row.IsDefault ? null : row.Id);
    }

    /// <summary>
    /// 將 <paramref name="id"/> 寫入 DefaultCashAccountId 並同步更新每一列的 IsDefault
    /// 徽章；同時儲存到 AppSettings。
    /// </summary>
    private async Task ApplyDefaultCashAccountAsync(Guid? id)
    {
        DefaultCashAccountId = id;
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
        DefaultCashAccountId is { } id
            ? CashAccounts.FirstOrDefault(r => r.Id == id)
            : null;

    /// <summary>
    /// When true, archived (soft-deleted) accounts are shown in the Cash list.
    /// Plan Task 14 + Task 19 (XAML toggle to be wired later).
    /// </summary>
    [ObservableProperty] private bool _showArchivedAccounts;

    partial void OnShowArchivedAccountsChanged(bool value)
        => _ = LoadCashAccountsAsync();

    private async Task LoadCashAccountsAsync()
    {
        if (_assetRepo is null)
            return;
        var accounts = await _assetRepo.GetItemsByTypeAsync(Core.Models.FinancialType.Asset);
        // Bulk-project all cash balances from the trade journal in one pass.
        var balances = await _balanceQuery.GetAllCashBalancesAsync();
        CashAccounts.Clear();
        foreach (var a in accounts)
        {
            if (!ShowArchivedAccounts && !a.IsActive) continue;
            var bal = balances.TryGetValue(a.Id, out var v) ? v : 0m;
            CashAccounts.Add(new CashAccountRowViewModel(a, bal));
        }
        HasNoCashAccounts = CashAccounts.Count == 0;

        var savedId = _settingsService?.Current?.DefaultCashAccountId;
        if (savedId.HasValue && CashAccounts.All(r => r.Id != savedId.Value))
            savedId = null;
        DefaultCashAccountId = savedId;
        foreach (var r in CashAccounts)
            r.IsDefault = savedId.HasValue && r.Id == savedId.Value;

        // Refresh Lazy-Upsert suggestion list for TX form editable ComboBox (Task 19).
        // Only include active accounts — archived ones stay out of the dropdown.
        CashAccountSuggestions.Clear();
        foreach (var a in accounts.Where(a => a.IsActive).OrderBy(a => a.Name))
            CashAccountSuggestions.Add(a.Name);
    }

    private async Task LoadLiabilitiesAsync()
    {
        var snapshots = await _balanceQuery.GetAllLiabilitySnapshotsAsync();
        Liabilities.Clear();
        foreach (var (label, snap) in snapshots.OrderBy(kv => kv.Key))
            Liabilities.Add(new LiabilityRowViewModel(label, snap));
        HasNoLiabilities = Liabilities.Count == 0;
        // Notify ComboBox in LoanTxForm to refresh its suggestion list.
        OnPropertyChanged(nameof(LoanLabelSuggestions));
    }

    // Account detail side-panel (Cash / Liability) — AscentPortfolio pattern
    //
    // Clicking a row on the Cash or Liability tab opens a right-side panel with
    // tabs (概覽 / 交易記錄), edit & delete controls, and summary stats. This
    // replaces the inline Actions column (edit / delete buttons) that used to
    // live inside the DataGrid — row content is cleaner and the actions have
    // room to breathe.

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedCashRow))]
    [NotifyPropertyChangedFor(nameof(SelectedCashTrades))]
    [NotifyPropertyChangedFor(nameof(SelectedCashTotalDeposits))]
    [NotifyPropertyChangedFor(nameof(SelectedCashTotalWithdrawals))]
    private CashAccountRowViewModel? _selectedCashRow;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedLiabilityRow))]
    [NotifyPropertyChangedFor(nameof(SelectedLiabilityTrades))]
    [NotifyPropertyChangedFor(nameof(SelectedLiabilityTotalBorrows))]
    [NotifyPropertyChangedFor(nameof(SelectedLiabilityTotalRepays))]
    private LiabilityRowViewModel? _selectedLiabilityRow;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedPositionRow))]
    [NotifyPropertyChangedFor(nameof(SelectedPositionTrades))]
    [NotifyPropertyChangedFor(nameof(SelectedPositionDividendIncome))]
    [NotifyPropertyChangedFor(nameof(SelectedPositionRealizedTotal))]
    [NotifyPropertyChangedFor(nameof(SelectedPositionTradeAvgPrice))]
    [NotifyPropertyChangedFor(nameof(HasSelectedPositionRealized))]
    private PortfolioRowViewModel? _selectedPositionRow;

    /// <summary>
    /// 是否有任何已實現損益資料（賣出價差或股息收入）。用於決定 Detail 面板的
    /// 「已實現損益」區塊是否顯示 — 無資料時隱藏，避免三行 NT$0 佔版面。
    /// </summary>
    public bool HasSelectedPositionRealized =>
        SelectedPositionRealizedTotal != 0m || SelectedPositionDividendIncome > 0m;

    public bool HasSelectedCashRow => SelectedCashRow is not null;
    public bool HasSelectedLiabilityRow => SelectedLiabilityRow is not null;
    public bool HasSelectedPositionRow => SelectedPositionRow is not null;

    /// <summary>
    /// Detail-panel active tab ("overview" or "trades"). Shared between the Cash and
    /// Liability panels — each panel only shows at most one at a time so a single
    /// property is sufficient; resets to "overview" whenever the selected row changes.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDetailOverviewTab))]
    [NotifyPropertyChangedFor(nameof(IsDetailTradesTab))]
    private string _detailTab = "overview";

    public bool IsDetailOverviewTab => DetailTab == "overview";
    public bool IsDetailTradesTab => DetailTab == "trades";

    partial void OnSelectedCashRowChanged(CashAccountRowViewModel? _) => DetailTab = "overview";
    partial void OnSelectedLiabilityRowChanged(LiabilityRowViewModel? _) => DetailTab = "overview";
    partial void OnSelectedPositionRowChanged(PortfolioRowViewModel? _) => DetailTab = "overview";

    // Cash account stats + filtered trades
    public IEnumerable<TradeRowViewModel> SelectedCashTrades =>
        SelectedCashRow is { } r
            ? Trades.Where(t => t.CashAccountId == r.Id)
                    .OrderByDescending(t => t.TradeDate)
            : [];

    public decimal SelectedCashTotalDeposits =>
        SelectedCashRow is { } r
            ? Trades.Where(t => t.CashAccountId == r.Id &&
                                (t.Type == TradeType.Deposit ||
                                 t.Type == TradeType.Income ||
                                 t.Type == TradeType.CashDividend ||
                                 t.Type == TradeType.LoanBorrow ||
                                 t.Type == TradeType.Sell))
                    .Sum(t => Math.Abs(t.CashAmount ?? 0))
            : 0m;

    public decimal SelectedCashTotalWithdrawals =>
        SelectedCashRow is { } r
            ? Trades.Where(t => t.CashAccountId == r.Id &&
                                (t.Type == TradeType.Withdrawal ||
                                 t.Type == TradeType.Buy ||
                                 t.Type == TradeType.LoanRepay))
                    .Sum(t => Math.Abs(t.CashAmount ?? 0))
            : 0m;

    // Liability stats + filtered trades
    public IEnumerable<TradeRowViewModel> SelectedLiabilityTrades =>
        SelectedLiabilityRow is { } r
            ? Trades.Where(t => (t.Type == TradeType.LoanBorrow || t.Type == TradeType.LoanRepay) &&
                                t.LoanLabel == r.Label)
                    .OrderByDescending(t => t.TradeDate)
            : [];

    public decimal SelectedLiabilityTotalBorrows =>
        SelectedLiabilityRow is { } r
            ? Trades.Where(t => t.Type == TradeType.LoanBorrow && t.LoanLabel == r.Label)
                    .Sum(t => t.CashAmount ?? 0)
            : 0m;

    public decimal SelectedLiabilityTotalRepays =>
        SelectedLiabilityRow is { } r
            ? Trades.Where(t => t.Type == TradeType.LoanRepay && t.LoanLabel == r.Label)
                    .Sum(t => t.CashAmount ?? 0)
            : 0m;

    // Investment position stats + filtered trades
    public IEnumerable<TradeRowViewModel> SelectedPositionTrades =>
        SelectedPositionRow is { } r
            ? Trades.Where(t => string.Equals(t.Symbol, r.Symbol, StringComparison.OrdinalIgnoreCase) &&
                                (t.Type == TradeType.Buy || t.Type == TradeType.Sell ||
                                 t.Type == TradeType.CashDividend || t.Type == TradeType.StockDividend))
                    .OrderByDescending(t => t.TradeDate)
            : [];

    /// <summary>
    /// 成交均價 — 以 Buy 交易紀錄的股數加權平均，**不含買入手續費**。
    /// 對應側面板上「跟交易記錄手動加總」的結果，讓使用者能直接對帳。
    /// 相對地 <see cref="PortfolioRowViewModel.BuyPrice"/> 是含手續費攤平的「成本均價」。
    /// </summary>
    public decimal SelectedPositionTradeAvgPrice
    {
        get
        {
            if (SelectedPositionRow is not { } r)
                return 0m;
            var buys = Trades
                .Where(t => t.IsBuy && string.Equals(t.Symbol, r.Symbol, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (buys.Count == 0)
                return 0m;
            var totalQty  = buys.Sum(t => (decimal)t.Quantity);
            var totalGross = buys.Sum(t => t.Price * t.Quantity);
            return totalQty > 0 ? totalGross / totalQty : 0m;
        }
    }

    /// <summary>Sum of all CashDividend trades for the selected position (gross dividend income).</summary>
    public decimal SelectedPositionDividendIncome =>
        SelectedPositionRow is { } r
            ? Trades.Where(t => string.Equals(t.Symbol, r.Symbol, StringComparison.OrdinalIgnoreCase) &&
                                t.Type == TradeType.CashDividend)
                    .Sum(t => t.CashAmount ?? 0)
            : 0m;

    /// <summary>
    /// Realized capital gain — proper FIFO/avg-cost lot matching is deferred to a later
    /// pass; for now we report 0 as a placeholder so the realized P&L card layout still
    /// makes sense for users with no sells. (Matches reference design which also shows $0.)
    /// </summary>
    public decimal SelectedPositionCapitalGain => 0m;

    public decimal SelectedPositionRealizedTotal =>
        SelectedPositionDividendIncome + SelectedPositionCapitalGain;

    [RelayCommand]
    private void CloseCashDetail() => SelectedCashRow = null;

    [RelayCommand]
    private void CloseLiabilityDetail() => SelectedLiabilityRow = null;

    [RelayCommand]
    private void ClosePositionDetail() => SelectedPositionRow = null;

    /// <summary>
    /// Permanently removes a position entry. Trade history rows for the symbol are
    /// intentionally left in place — the user can clean them up from the trades tab if
    /// desired. Mirrors the cash / liability removal flow (confirm dialog → repo → UI).
    /// </summary>
    [RelayCommand]
    private void RemovePosition(PortfolioRowViewModel row)
    {
        if (row is null)
            return;
        var msg = Application.Current?.TryFindResource("Portfolio.Detail.DeleteWarning") as string
                  ?? "刪除後無法復原，請確認不再需要後再操作。";
        AskConfirm(msg, async () =>
        {
            foreach (var id in row.AllEntryIds)
                await _repo.RemoveAsync(id);
            Positions.Remove(row);
            if (ReferenceEquals(SelectedPositionRow, row))
                SelectedPositionRow = null;
            RebuildTotals();
        });
    }

    [RelayCommand]
    private void SwitchDetailTab(string tab) => DetailTab = tab;

}
