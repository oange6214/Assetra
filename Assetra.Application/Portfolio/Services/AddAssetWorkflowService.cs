using System.Text.RegularExpressions;
using Assetra.Application.Portfolio.Contracts;
using Assetra.Application.Portfolio.Dtos;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Assetra.Core.Trading;

namespace Assetra.Application.Portfolio.Services;

public sealed class AddAssetWorkflowService : IAddAssetWorkflowService
{
    private readonly IStockSearchService _searchService;
    private readonly IStockHistoryProvider? _historyProvider;
    private readonly IPortfolioRepository? _portfolioRepository;
    private readonly IPortfolioPositionLogRepository? _positionLogRepository;
    private readonly ITransactionService? _transactionService;
    private readonly ISymbolDirectory? _symbolDirectory;

    public AddAssetWorkflowService(
        IStockSearchService searchService,
        IStockHistoryProvider? historyProvider = null,
        IPortfolioRepository? portfolioRepository = null,
        IPortfolioPositionLogRepository? positionLogRepository = null,
        ITransactionService? transactionService = null,
        ISymbolDirectory? symbolDirectory = null)
    {
        _searchService = searchService;
        _historyProvider = historyProvider;
        _portfolioRepository = portfolioRepository;
        _positionLogRepository = positionLogRepository;
        _transactionService = transactionService;
        _symbolDirectory = symbolDirectory;
    }

    public IReadOnlyList<StockSearchResult> SearchSymbols(string query, int maxResults = 8)
    {
        if (string.IsNullOrWhiteSpace(query) || maxResults <= 0)
            return [];
        return ((_symbolDirectory?.Search(query.Trim()) ?? _searchService.Search(query.Trim())) ?? [])
            .Take(maxResults)
            .ToList();
    }

    public async Task<ClosePriceLookupResult> LookupClosePriceAsync(
        string symbol,
        DateTime buyDate,
        string? exchange = null,
        CancellationToken ct = default)
    {
        if (_historyProvider is null)
            return new ClosePriceLookupResult(false, null, string.Empty);

        await Task.Delay(300, ct).ConfigureAwait(false);

        var normalized = symbol.Trim().ToUpperInvariant();
        var targetDate = DateOnly.FromDateTime(buyDate);
        var resolvedExchange = ResolveExchange(normalized, exchange);
        var daysDiff = (DateTime.Today - buyDate).Days;
        var period = daysDiff <= 35 ? ChartPeriod.OneMonth
                   : daysDiff <= 100 ? ChartPeriod.ThreeMonths
                   : daysDiff <= 370 ? ChartPeriod.OneYear
                   : ChartPeriod.TwoYears;

        var history = await _historyProvider.GetHistoryAsync(normalized, resolvedExchange, period, ct).ConfigureAwait(false);
        var point = history
            .Where(h => h.Date <= targetDate)
            .OrderByDescending(h => h.Date)
            .FirstOrDefault();

        if (point is null)
            return new ClosePriceLookupResult(false, null, "查無收盤資料，請手動輸入");

        var hint = point.Date == targetDate
            ? $"已帶入 {targetDate:yyyy/MM/dd} 收盤價"
            : $"已帶入最近交易日 {point.Date:yyyy/MM/dd} 收盤價";
        return new ClosePriceLookupResult(true, point.Close, hint);
    }

    public BuyPreviewResult BuildBuyPreview(BuyPreviewRequest request)
    {
        var gross = request.Price * request.Quantity;
        decimal commission;

        if (request.ManualFee is >= 0)
        {
            commission = request.ManualFee.Value;
        }
        else
        {
            var symbol = request.Symbol.Trim();
            var exchange = ResolveExchange(symbol, request.Exchange);
            if (UsesTaiwanTradeCost(exchange))
            {
                var resolved = ResolveSymbol(symbol, exchange);
                var isEtf = resolved?.IsEtf ?? _searchService.IsEtf(symbol);
                commission = TaiwanTradeFeeCalculator
                    .CalcBuy(request.Price, request.Quantity, request.CommissionDiscount, isEtf)
                    .Commission;
            }
            else
            {
                commission = 0m;
            }
        }

        var totalCost = gross + commission;
        return new BuyPreviewResult(
            gross,
            commission,
            totalCost,
            request.Quantity > 0 ? totalCost / request.Quantity : 0m);
    }

    public async Task<PortfolioEntry> EnsureStockEntryAsync(
        EnsureStockEntryRequest request,
        CancellationToken ct = default)
    {
        EnsurePersistenceDependencies();

        var symbol = request.Symbol.Trim().ToUpperInvariant();
        var resolved = ResolveSymbol(symbol, request.Exchange);
        var exchange = request.Exchange ?? resolved?.Exchange ?? _searchService.GetExchange(symbol) ?? InferExchange(symbol);
        var name = request.Name ?? resolved?.Name ?? _searchService.GetName(symbol) ?? string.Empty;

        // 幣別優先順序：caller 明確指定 > 由 symbol-directory resolve > 由 exchange 推導預設。
        // Watchlist 對話框會明確帶 Currency（讓使用者自選 USD/TWD/HKD…）。
        var currency = !string.IsNullOrWhiteSpace(request.Currency)
            ? request.Currency!.Trim().ToUpperInvariant()
            : resolved?.Currency ?? StockExchangeRegistry.ResolveDefaultCurrency(exchange);
        // AssetType 也是 caller 主導；Buy 流程預設 Stock，Watchlist 可指定 Fund/Bond/Etf 等。
        var assetType = request.AssetType;
        var isEtf = assetType == AssetType.Etf || (resolved?.IsEtf ?? _searchService.IsEtf(symbol));
        var entryId = await _portfolioRepository!
            .FindOrCreatePortfolioEntryAsync(symbol, exchange, name, assetType, currency, isEtf, request.PortfolioGroupId, ct)
            .ConfigureAwait(false);
        await _portfolioRepository.UnarchiveAsync(entryId).ConfigureAwait(false);
        return new PortfolioEntry(entryId, symbol, exchange, assetType, name, currency, IsActive: true, IsEtf: isEtf,
            PortfolioGroupId: request.PortfolioGroupId);
    }

    public async Task<StockBuyResult> ExecuteStockBuyAsync(
        StockBuyRequest request,
        CancellationToken ct = default)
    {
        EnsurePersistenceDependencies();

        var symbol = request.Symbol.Trim().ToUpperInvariant();
        var resolved = ResolveSymbol(symbol, request.Exchange);
        var exchange = request.Exchange ?? resolved?.Exchange ?? _searchService.GetExchange(symbol) ?? InferExchange(symbol);
        var name = request.Name ?? resolved?.Name ?? _searchService.GetName(symbol) ?? string.Empty;
        var preview = BuildBuyPreview(new BuyPreviewRequest(
            symbol,
            request.Price,
            request.Quantity,
            request.CommissionDiscount,
            request.ManualFee,
            exchange));
        decimal? discountForRecord = request.ManualFee is >= 0 ? null : request.CommissionDiscount;

        var entry = await EnsureStockEntryAsync(
            new EnsureStockEntryRequest(symbol, exchange, name, request.PortfolioGroupId),
            ct).ConfigureAwait(false);

        await _positionLogRepository!.LogAsync(new PortfolioPositionLog(
            Guid.NewGuid(),
            DateOnly.FromDateTime(DateTime.Today),
            entry.Id,
            symbol,
            exchange,
            request.Quantity,
            preview.CostPerShare)).ConfigureAwait(false);

        var tradeDate = request.BuyDate.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(request.BuyDate, DateTimeKind.Local).ToUniversalTime()
            : request.BuyDate.ToUniversalTime();

        var trade = new Trade(
            Id: Guid.NewGuid(),
            Symbol: symbol,
            Exchange: exchange,
            Name: name,
            Type: TradeType.Buy,
            TradeDate: tradeDate,
            Price: request.Price,
            Quantity: request.Quantity,
            RealizedPnl: null,
            RealizedPnlPct: null,
            CashAmount: request.ActualCashAmount ?? preview.TotalCost,
            CashAccountId: request.CashAccountId,
            Note: null,
            PortfolioEntryId: entry.Id,
            Commission: preview.Commission,
            CommissionDiscount: discountForRecord,
            // MultiCurrency-Trade-Refactor P2 — 從 exchange 自動推導標的計價幣別。
            // 用 Core 既有的 StockExchangeRegistry（同份 TWSE/TPEX/NYSE/... → currency 對照表，
            // 跟 IsCrossCurrencyCashDebit 走同一個 source of truth，避免兩份 mapping 漂移）。
            InstrumentCurrency: Assetra.Core.Models.StockExchangeRegistry.ResolveDefaultCurrency(exchange),
            // P3 — 跨幣別交易時帶入 FX rate。同幣別 (null) 保持 implicit 1.0 寫法。
            FxRate: request.FxRate,
            // Portfolio-Groups-Refactor P3 — 群組（bucket）。null 由 repo fallback 成 DefaultId。
            PortfolioGroupId: request.PortfolioGroupId);
        await _transactionService!.RecordAsync(trade).ConfigureAwait(false);

        return new StockBuyResult(
            entry,
            preview.Commission,
            discountForRecord,
            preview.CostPerShare);
    }

    public async Task<ManualAssetCreateResult> CreateManualAssetAsync(
        ManualAssetCreateRequest request,
        CancellationToken ct = default)
    {
        EnsurePersistenceDependencies();
        ct.ThrowIfCancellationRequested();

        var entry = new PortfolioEntry(
            Guid.NewGuid(),
            request.Symbol.Trim(),
            request.Exchange.Trim(),
            request.AssetType,
            request.Name.Trim(),
            PortfolioGroupId: request.PortfolioGroupId);
        await _portfolioRepository!.AddAsync(entry).ConfigureAwait(false);

        var snapshot = new PositionSnapshot(
            entry.Id,
            request.Quantity,
            request.TotalCost,
            request.UnitPrice,
            0m,
            request.AcquiredOn);

        return new ManualAssetCreateResult(entry, snapshot);
    }

    /// <summary>
    /// 沒有 symbol-directory / autocomplete 資料時的最後 fallback。規則依序：
    /// <list type="number">
    ///   <item>5 碼數字 + 1 英文字（如 <c>00981A</c>）→ <c>TPEX</c></item>
    ///   <item>4 碼數字（如 <c>2330</c>、<c>0050</c>）→ <c>TWSE</c></item>
    ///   <item>1–5 個英文字（可選 <c>.X</c> 後綴，如 <c>F</c>、<c>SPY</c>、<c>DRAM</c>、<c>BRK.B</c>）→ <c>NASDAQ</c></item>
    ///   <item>其他（含數字 + 字母混合的非標準格式）→ <c>TWSE</c>（保守 fallback）</item>
    /// </list>
    /// <para>
    /// 第 3 條規則的設計重點：把所有 plausible US ticker 都導到 NASDAQ。理由：
    /// (a) TwelveData provider 的 CanHandle 對 NASDAQ/NYSE/NYSEARCA/AMEX/BATS/IEX 都接，
    ///     真實後端 API 只看 symbol，exchange tag 在這層只是 routing key；分錯也能拿到報價。
    /// (b) YahooSymbolMapper 對 NASDAQ 直接回 bare symbol，跟其他美股 venue 一致。
    /// (c) 之前 fallback 是 TWSE，導致 DRAM 之類 ETF 被當成台股查不到報價而 0；
    ///     換到 NASDAQ 即使猜錯 venue 也至少能拿到正確報價。
    /// </para>
    /// <para>
    /// 不會誤判台股：所有台股代號（2330/0050/00981A）都含數字，永遠走第 1/2 條規則。
    /// </para>
    /// </summary>
    public string InferExchange(string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            return "TWSE";
        var s = symbol.Trim().ToUpperInvariant();
        if (Regex.IsMatch(s, @"^\d{5}[A-Z]$"))
            return "TPEX";
        if (Regex.IsMatch(s, @"^\d{4}$"))
            return "TWSE";
        if (Regex.IsMatch(s, @"^[A-Z]{1,5}(\.[A-Z])?$"))
            return "NASDAQ";
        return "TWSE";
    }

    private StockSearchResult? ResolveSymbol(string symbol, string? exchange = null) =>
        _symbolDirectory?.Resolve(symbol, exchange);

    private string ResolveExchange(string symbol, string? exchange = null)
    {
        if (!string.IsNullOrWhiteSpace(exchange))
            return exchange.Trim().ToUpperInvariant();

        return ResolveSymbol(symbol)?.Exchange
               ?? _searchService.GetExchange(symbol)
               ?? InferExchange(symbol);
    }

    private static bool UsesTaiwanTradeCost(string exchange) =>
        string.IsNullOrWhiteSpace(exchange) ||
        string.Equals(exchange, "TWSE", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(exchange, "TPEX", StringComparison.OrdinalIgnoreCase);

    private void EnsurePersistenceDependencies()
    {
        if (_portfolioRepository is null || _positionLogRepository is null || _transactionService is null)
            throw new InvalidOperationException("Add-asset persistence dependencies are not configured.");
    }
}
