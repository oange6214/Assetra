using Assetra.Application.Analysis;
using Assetra.Application.Portfolio.Contracts;
using Assetra.Application.Portfolio.Dtos;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;

namespace Assetra.Application.Portfolio.Services;

public sealed class SellWorkflowService : ISellWorkflowService
{
    private readonly ITradeRepository _tradeRepository;
    private readonly IPortfolioRepository _portfolioRepository;
    private readonly IPortfolioPositionLogRepository _positionLogRepository;
    private readonly IPositionQueryService _positionQueryService;
    // MultiCurrency-Reporting P4.5b — optional FX history for the realized PnL split.
    // Null = no breakdown computed (Trade.RealizedMarketPnl/RealizedFxPnl stay null → UI "—").
    private readonly IFxRateHistoryService? _fxHistory;

    public SellWorkflowService(
        ITradeRepository tradeRepository,
        IPortfolioRepository portfolioRepository,
        IPortfolioPositionLogRepository positionLogRepository,
        IPositionQueryService positionQueryService,
        IFxRateHistoryService? fxHistory = null)
    {
        _tradeRepository = tradeRepository;
        _portfolioRepository = portfolioRepository;
        _positionLogRepository = positionLogRepository;
        _positionQueryService = positionQueryService;
        _fxHistory = fxHistory;
    }

    public async Task<SellWorkflowResult> RecordAsync(
        SellWorkflowRequest request,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var tradeDate = request.TradeDate.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(request.TradeDate, DateTimeKind.Local).ToUniversalTime()
            : request.TradeDate.ToUniversalTime();
        var realizedPnl = await _positionQueryService
            .ComputeRealizedPnlAsync(
                request.PortfolioEntryId,
                tradeDate,
                request.SellPrice,
                request.SellQuantity,
                request.Commission)
            .ConfigureAwait(false);
        var buyCost = request.BuyPrice * request.SellQuantity;
        var realizedPnlPct = buyCost > 0 ? realizedPnl / buyCost * 100m : 0m;

        // MultiCurrency-Reporting P4.5b — split realized PnL into market vs FX gain.
        // Look up sell-date + earliest-buy-date FX rates. The earliest-buy
        // simplification: when multiple lots feed one sell, use the FIRST buy's
        // date for the entire buy-side FX. Most sells close 1-2 lots and the
        // rate doesn't drift far across a few days — accurate enough for v1.
        // Sophisticated weighted-avg FX per-lot is a future enhancement.
        var instrumentCcy = StockExchangeRegistry.ResolveDefaultCurrency(request.Exchange);
        var (rmPnl, rfxPnl) = await ComputeBreakdownAsync(
            request, tradeDate, instrumentCcy, ct).ConfigureAwait(false);

        var trade = new Trade(
            Id: Guid.NewGuid(),
            Symbol: request.Symbol,
            Exchange: request.Exchange,
            Name: request.Name,
            Type: TradeType.Sell,
            TradeDate: tradeDate,
            Price: request.SellPrice,
            Quantity: request.SellQuantity,
            RealizedPnl: realizedPnl,
            RealizedPnlPct: realizedPnlPct,
            // P3 — 跨幣別賣出時帶入「實際入帳」覆寫，避免用 SellPrice × Qty 估錯。
            CashAmount: request.ActualCashAmount,
            CashAccountId: request.CashAccountId,
            PortfolioEntryId: request.PortfolioEntryId,
            Commission: request.Commission,
            CommissionDiscount: request.CommissionDiscount,
            // MultiCurrency-Trade-Refactor P2 — Sell 跟 Buy 對稱，標的幣別由 exchange 推導
            // (用 StockExchangeRegistry，跟 Buy/Dividend 走同一份 mapping)。
            InstrumentCurrency: instrumentCcy,
            FxRate: request.FxRate,
            // Portfolio-Groups-Refactor P3
            PortfolioGroupId: request.PortfolioGroupId,
            // MultiCurrency-Reporting P4.5b — realized PnL market/FX split
            RealizedMarketPnl: rmPnl,
            RealizedFxPnl: rfxPnl);

        await _tradeRepository.AddAsync(trade).ConfigureAwait(false);

        var remainingQuantity = Math.Max(0, request.CurrentQuantity - request.SellQuantity);
        if (request.SellQuantity >= request.CurrentQuantity)
        {
            foreach (var entryId in request.EntryIdsToArchive)
                await _portfolioRepository.ArchiveAsync(entryId).ConfigureAwait(false);
        }

        await _positionLogRepository.LogAsync(new PortfolioPositionLog(
                Guid.NewGuid(),
                DateOnly.FromDateTime(tradeDate.ToLocalTime()),
                request.PortfolioEntryId,
                request.Symbol,
                request.Exchange,
                remainingQuantity,
                request.BuyPrice))
            .ConfigureAwait(false);

        return new SellWorkflowResult(trade, remainingQuantity);
    }

    /// <summary>
    /// MultiCurrency-Reporting P4.5b — compute the (RealizedMarketPnl, RealizedFxPnl)
    /// pair for a sell. Returns (null, null) when FX history isn't wired in,
    /// when the instrument is the base currency (no FX component), or when
    /// the buy date's FX rate can't be resolved.
    ///
    /// <para>Buy-date approximation: uses the earliest Buy for the same
    /// PortfolioEntry as the FX reference point. Multi-lot sells with widely
    /// spaced buy dates lose some precision; revisit if users complain.</para>
    /// </summary>
    private async Task<(decimal? RealizedMarket, decimal? RealizedFx)> ComputeBreakdownAsync(
        SellWorkflowRequest request,
        DateTime sellTradeDate,
        string instrumentCurrency,
        CancellationToken ct)
    {
        if (_fxHistory is null) return (null, null);

        // Hard-code base = "TWD" for now. P4.5c can take an IAppSettingsService dep
        // and read BaseCurrency dynamically. Most users in scope are TWD-base.
        const string baseCurrency = "TWD";
        if (string.Equals(instrumentCurrency, baseCurrency, StringComparison.OrdinalIgnoreCase))
        {
            // Same-currency: explicit "no FX component" — let calculator return 0 fx.
            var sameCcy = RealizedPnlBreakdownCalculator.Compute(
                sellPriceNative: request.SellPrice,
                buyAvgPriceNative: request.BuyPrice,
                quantity: request.SellQuantity,
                buyFxRate: 1m, sellFxRate: 1m);
            return (sameCcy?.MarketBase, sameCcy?.FxBase);
        }

        // Mixed-currency: look up both rates. Find earliest Buy date for this entry.
        var allTrades = await _tradeRepository
            .GetByPortfolioEntryIdsAsync(new[] { request.PortfolioEntryId }, ct)
            .ConfigureAwait(false);
        var earliestBuy = allTrades
            .Where(t => t.Type == TradeType.Buy)
            .OrderBy(t => t.TradeDate)
            .FirstOrDefault();
        if (earliestBuy is null) return (null, null);

        var buyDate = DateOnly.FromDateTime(earliestBuy.TradeDate);
        var sellDate = DateOnly.FromDateTime(sellTradeDate);
        var buyFx = await _fxHistory.GetRateAsync(buyDate, instrumentCurrency, baseCurrency, ct).ConfigureAwait(false);
        var sellFx = await _fxHistory.GetRateAsync(sellDate, instrumentCurrency, baseCurrency, ct).ConfigureAwait(false);

        var breakdown = RealizedPnlBreakdownCalculator.Compute(
            sellPriceNative: request.SellPrice,
            buyAvgPriceNative: request.BuyPrice,
            quantity: request.SellQuantity,
            buyFxRate: buyFx,
            sellFxRate: sellFx);

        return (breakdown?.MarketBase, breakdown?.FxBase);
    }
}
