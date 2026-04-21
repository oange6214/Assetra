using System.Text.RegularExpressions;
using Assetra.AppLayer.Portfolio.Contracts;
using Assetra.AppLayer.Portfolio.Dtos;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Assetra.Core.Trading;

namespace Assetra.AppLayer.Portfolio.Services;

public sealed class AddAssetWorkflowService : IAddAssetWorkflowService
{
    private readonly IStockSearchService _searchService;
    private readonly IStockHistoryProvider? _historyProvider;
    private readonly IPortfolioRepository? _portfolioRepository;
    private readonly IPortfolioPositionLogRepository? _positionLogRepository;
    private readonly ITransactionService? _transactionService;

    public AddAssetWorkflowService(
        IStockSearchService searchService,
        IStockHistoryProvider? historyProvider = null,
        IPortfolioRepository? portfolioRepository = null,
        IPortfolioPositionLogRepository? positionLogRepository = null,
        ITransactionService? transactionService = null)
    {
        _searchService = searchService;
        _historyProvider = historyProvider;
        _portfolioRepository = portfolioRepository;
        _positionLogRepository = positionLogRepository;
        _transactionService = transactionService;
    }

    public IReadOnlyList<StockSearchResult> SearchSymbols(string query, int maxResults = 8)
    {
        if (string.IsNullOrWhiteSpace(query) || maxResults <= 0)
            return [];
        return (_searchService.Search(query.Trim()) ?? [])
            .Take(maxResults)
            .ToList();
    }

    public async Task<ClosePriceLookupResult> LookupClosePriceAsync(
        string symbol,
        DateTime buyDate,
        CancellationToken ct = default)
    {
        if (_historyProvider is null)
            return new ClosePriceLookupResult(false, null, string.Empty);

        await Task.Delay(300, ct).ConfigureAwait(false);

        var normalized = symbol.Trim().ToUpperInvariant();
        var targetDate = DateOnly.FromDateTime(buyDate);
        var exchange = _searchService.GetExchange(normalized) ?? InferExchange(normalized);
        var daysDiff = (DateTime.Today - buyDate).Days;
        var period = daysDiff <= 35 ? ChartPeriod.OneMonth
                   : daysDiff <= 100 ? ChartPeriod.ThreeMonths
                   : daysDiff <= 370 ? ChartPeriod.OneYear
                   : ChartPeriod.TwoYears;

        var history = await _historyProvider.GetHistoryAsync(normalized, exchange, period, ct).ConfigureAwait(false);
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
            var isEtf = _searchService.IsEtf(request.Symbol.Trim());
            commission = TaiwanTradeFeeCalculator
                .CalcBuy(request.Price, request.Quantity, request.CommissionDiscount, isEtf)
                .Commission;
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
        var exchange = request.Exchange ?? _searchService.GetExchange(symbol) ?? InferExchange(symbol);
        var name = request.Name ?? _searchService.GetName(symbol) ?? string.Empty;

        var entryId = await _portfolioRepository!
            .FindOrCreatePortfolioEntryAsync(symbol, exchange, name, AssetType.Stock, ct)
            .ConfigureAwait(false);
        await _portfolioRepository.UnarchiveAsync(entryId).ConfigureAwait(false);
        return new PortfolioEntry(entryId, symbol, exchange, AssetType.Stock, name);
    }

    public async Task<StockBuyResult> ExecuteStockBuyAsync(
        StockBuyRequest request,
        CancellationToken ct = default)
    {
        EnsurePersistenceDependencies();

        var symbol = request.Symbol.Trim().ToUpperInvariant();
        var exchange = request.Exchange ?? _searchService.GetExchange(symbol) ?? InferExchange(symbol);
        var name = request.Name ?? _searchService.GetName(symbol) ?? string.Empty;
        var preview = BuildBuyPreview(new BuyPreviewRequest(
            symbol,
            request.Price,
            request.Quantity,
            request.CommissionDiscount,
            request.ManualFee));
        decimal? discountForRecord = request.ManualFee is >= 0 ? null : request.CommissionDiscount;

        var entry = await EnsureStockEntryAsync(
            new EnsureStockEntryRequest(symbol, exchange, name),
            ct).ConfigureAwait(false);

        await _positionLogRepository!.LogAsync(new PortfolioPositionLog(
            Guid.NewGuid(),
            DateOnly.FromDateTime(DateTime.Today),
            entry.Id,
            symbol,
            exchange,
            request.Quantity,
            preview.CostPerShare)).ConfigureAwait(false);

        var trade = new Trade(
            Id: Guid.NewGuid(),
            Symbol: symbol,
            Exchange: exchange,
            Name: name,
            Type: TradeType.Buy,
            TradeDate: request.BuyDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            Price: request.Price,
            Quantity: request.Quantity,
            RealizedPnl: null,
            RealizedPnlPct: null,
            CashAmount: null,
            CashAccountId: request.CashAccountId,
            Note: null,
            PortfolioEntryId: entry.Id,
            Commission: preview.Commission,
            CommissionDiscount: discountForRecord);
        await _transactionService!.RecordAsync(trade).ConfigureAwait(false);

        return new StockBuyResult(
            entry,
            preview.Commission,
            discountForRecord,
            preview.CostPerShare);
    }

    public string InferExchange(string symbol) =>
        Regex.IsMatch(symbol, @"^\d{5}[A-Z]$")
            ? "TPEX"
            : "TWSE";

    private void EnsurePersistenceDependencies()
    {
        if (_portfolioRepository is null || _positionLogRepository is null || _transactionService is null)
            throw new InvalidOperationException("Add-asset persistence dependencies are not configured.");
    }
}
