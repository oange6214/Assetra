using Assetra.Application.Portfolio.Contracts;
using Assetra.Application.Portfolio.Dtos;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;

namespace Assetra.Application.Portfolio.Services;

public sealed class TransactionWorkflowService : ITransactionWorkflowService
{
    private readonly ITransactionService _txService;

    public TransactionWorkflowService(ITransactionService txService)
    {
        _txService = txService;
    }

    public async Task RecordCashDividendAsync(CashDividendTransactionRequest request, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var mainTrade = new Trade(
            Id: Guid.NewGuid(),
            Symbol: request.Symbol,
            Exchange: request.Exchange,
            Name: request.Name,
            Type: TradeType.CashDividend,
            TradeDate: request.TradeDate,
            Price: request.PerShare,
            Quantity: request.Quantity,
            RealizedPnl: null,
            RealizedPnlPct: null,
            // P3 — 跨幣別股息：使用者填的 ActualCashAmount（帳戶幣別）覆寫
            // PerShare × Quantity（標的幣別）。同幣別交易 fallback 為原本算法。
            CashAmount: request.ActualCashAmount ?? request.TotalAmount,
            CashAccountId: request.CashAccountId,
            Note: null,
            // MultiCurrency-Trade-Refactor P2 — 股息標的幣別跟 Buy/Sell 一致
            InstrumentCurrency: Assetra.Core.Models.StockExchangeRegistry.ResolveDefaultCurrency(request.Exchange),
            FxRate: request.FxRate,
            // Portfolio-Groups-Refactor P3
            PortfolioGroupId: request.PortfolioGroupId);

        await _txService.RecordAsync(mainTrade).ConfigureAwait(false);

        if (request.Fee > 0)
        {
            await _txService.RecordAsync(CreateFeeTrade(
                request.Fee,
                request.CashAccountId,
                request.TradeDate,
                $"{request.Name} 股息手續費",
                null,
                mainTrade.Id)).ConfigureAwait(false);
        }
    }

    public async Task RecordStockDividendAsync(StockDividendTransactionRequest request, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var trade = new Trade(
            Id: Guid.NewGuid(),
            Symbol: request.Symbol,
            Exchange: request.Exchange,
            Name: request.Name,
            Type: TradeType.StockDividend,
            TradeDate: request.TradeDate,
            Price: 0,
            Quantity: request.NewShares,
            RealizedPnl: null,
            RealizedPnlPct: null,
            CashAmount: null,
            CashAccountId: null,
            Note: null,
            PortfolioEntryId: request.PortfolioEntryId,
            // MultiCurrency-Trade-Refactor P2 — 配股標的幣別跟標的本身一致
            InstrumentCurrency: Assetra.Core.Models.StockExchangeRegistry.ResolveDefaultCurrency(request.Exchange));

        await _txService.RecordAsync(trade).ConfigureAwait(false);
    }

    public async Task RecordIncomeAsync(IncomeTransactionRequest request, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        // Name = AccountName so the trade-list "資產" column shows the cash
        // account ("台新 Richart") for income — same convention as Deposit /
        // Withdrawal. Previously this field was assigned request.Note, which
        // overlapped with Trade.Note + Trade.CategoryId and made all three
        // columns display the same string ("薪資"); editing the category
        // appeared to mutate the asset column. Decoupling Name from Note
        // eliminates that confusion.
        var mainTrade = new Trade(
            Id: Guid.NewGuid(),
            Symbol: string.Empty,
            Exchange: string.Empty,
            Name: request.AccountName,
            Type: TradeType.Income,
            TradeDate: request.TradeDate,
            Price: 0,
            Quantity: 1,
            RealizedPnl: null,
            RealizedPnlPct: null,
            CashAmount: request.Amount,
            CashAccountId: request.CashAccountId,
            Note: request.Note,
            CategoryId: request.CategoryId);

        await _txService.RecordAsync(mainTrade).ConfigureAwait(false);

        if (request.Fee > 0)
        {
            await _txService.RecordAsync(CreateFeeTrade(
                request.Fee,
                request.CashAccountId,
                request.TradeDate,
                $"{request.AccountName} 手續費",
                request.Note,
                mainTrade.Id)).ConfigureAwait(false);
        }
    }

    public async Task RecordCashFlowAsync(CashFlowTransactionRequest request, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var mainTrade = new Trade(
            Id: Guid.NewGuid(),
            Symbol: request.AccountName,
            Exchange: string.Empty,
            Name: request.AccountName,
            Type: request.Type,
            TradeDate: request.TradeDate,
            Price: 0,
            Quantity: 1,
            RealizedPnl: null,
            RealizedPnlPct: null,
            CashAmount: request.Amount,
            CashAccountId: request.CashAccountId,
            Note: request.Note,
            CategoryId: request.CategoryId);

        await _txService.RecordAsync(mainTrade).ConfigureAwait(false);

        if (request.Fee > 0)
        {
            await _txService.RecordAsync(CreateFeeTrade(
                request.Fee,
                request.CashAccountId,
                request.TradeDate,
                $"{request.AccountName} 手續費",
                request.Note,
                mainTrade.Id)).ConfigureAwait(false);
        }
    }

    public async Task RecordTransferAsync(TransferTransactionRequest request, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        Guid feeParentId;

        if (request.SourceAmount == request.DestinationAmount)
        {
            var transfer = new Trade(
                Id: Guid.NewGuid(),
                Symbol: request.SourceName,
                Exchange: string.Empty,
                Name: $"{request.SourceName} → {request.DestinationName}",
                Type: TradeType.Transfer,
                TradeDate: request.TradeDate,
                Price: 0,
                Quantity: 1,
                RealizedPnl: null,
                RealizedPnlPct: null,
                CashAmount: request.SourceAmount,
                CashAccountId: request.SourceCashAccountId,
                Note: request.Note,
                ToCashAccountId: request.DestinationCashAccountId);
            await _txService.RecordAsync(transfer).ConfigureAwait(false);
            feeParentId = transfer.Id;
        }
        else
        {
            var withdrawNote = string.IsNullOrWhiteSpace(request.Note)
                ? $"轉帳 → {request.DestinationName}"
                : $"轉帳 → {request.DestinationName} — {request.Note}";
            var withdraw = new Trade(
                Id: Guid.NewGuid(),
                Symbol: request.SourceName,
                Exchange: string.Empty,
                Name: request.SourceName,
                Type: TradeType.Withdrawal,
                TradeDate: request.TradeDate,
                Price: 0,
                Quantity: 1,
                RealizedPnl: null,
                RealizedPnlPct: null,
                CashAmount: request.SourceAmount,
                CashAccountId: request.SourceCashAccountId,
                Note: withdrawNote);
            await _txService.RecordAsync(withdraw).ConfigureAwait(false);
            feeParentId = withdraw.Id;

            var depositNote = string.IsNullOrWhiteSpace(request.Note)
                ? $"轉帳 ← {request.SourceName}"
                : $"轉帳 ← {request.SourceName} — {request.Note}";
            await _txService.RecordAsync(new Trade(
                Id: Guid.NewGuid(),
                Symbol: request.DestinationName,
                Exchange: string.Empty,
                Name: request.DestinationName,
                Type: TradeType.Deposit,
                TradeDate: request.TradeDate,
                Price: 0,
                Quantity: 1,
                RealizedPnl: null,
                RealizedPnlPct: null,
                CashAmount: request.DestinationAmount,
                CashAccountId: request.DestinationCashAccountId,
                Note: depositNote)).ConfigureAwait(false);
        }

        if (request.Fee > 0)
        {
            await _txService.RecordAsync(CreateFeeTrade(
                request.Fee,
                request.SourceCashAccountId,
                request.TradeDate,
                $"轉帳手續費 ({request.SourceName} → {request.DestinationName})",
                request.Note,
                feeParentId)).ConfigureAwait(false);
        }
    }

    private static Trade CreateFeeTrade(
        decimal fee,
        Guid? cashAccountId,
        DateTime tradeDate,
        string notePrefix,
        string? userNote,
        Guid parentTradeId)
    {
        return new Trade(
            Id: Guid.NewGuid(),
            Symbol: string.Empty,
            Exchange: string.Empty,
            Name: "手續費",
            Type: TradeType.Withdrawal,
            TradeDate: tradeDate,
            Price: 0,
            Quantity: 1,
            RealizedPnl: null,
            RealizedPnlPct: null,
            CashAmount: fee,
            CashAccountId: cashAccountId,
            Note: string.IsNullOrWhiteSpace(userNote)
                ? notePrefix
                : $"{notePrefix} — {userNote}",
            ParentTradeId: parentTradeId);
    }
}
