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

    public SellWorkflowService(
        ITradeRepository tradeRepository,
        IPortfolioRepository portfolioRepository,
        IPortfolioPositionLogRepository positionLogRepository,
        IPositionQueryService positionQueryService)
    {
        _tradeRepository = tradeRepository;
        _portfolioRepository = portfolioRepository;
        _positionLogRepository = positionLogRepository;
        _positionQueryService = positionQueryService;
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
            InstrumentCurrency: Assetra.Core.Models.StockExchangeRegistry.ResolveDefaultCurrency(request.Exchange),
            FxRate: request.FxRate,
            // Portfolio-Groups-Refactor P3
            PortfolioGroupId: request.PortfolioGroupId);

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
}
