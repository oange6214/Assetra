using Assetra.AppLayer.Portfolio.Contracts;
using Assetra.AppLayer.Portfolio.Dtos;
using Assetra.Core.Models;
using Assetra.Core.Services;

namespace Assetra.AppLayer.Portfolio.Services;

public sealed class TransactionWorkflowService : ITransactionWorkflowService
{
    public TransactionWorkflowPlan CreateCashDividendPlan(CashDividendTransactionRequest request)
    {
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
            CashAmount: request.TotalAmount,
            CashAccountId: request.CashAccountId,
            Note: null);

        return new TransactionWorkflowPlan(
            BuildTrades(mainTrade, request.Fee, request.CashAccountId, request.TradeDate,
                $"{request.Name} 股息手續費", null));
    }

    public TransactionWorkflowPlan CreateStockDividendPlan(StockDividendTransactionRequest request)
    {
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
            PortfolioEntryId: request.PortfolioEntryId);

        return new TransactionWorkflowPlan([trade]);
    }

    public TransactionWorkflowPlan CreateIncomePlan(IncomeTransactionRequest request)
    {
        var mainTrade = new Trade(
            Id: Guid.NewGuid(),
            Symbol: string.Empty,
            Exchange: string.Empty,
            Name: request.Note,
            Type: TradeType.Income,
            TradeDate: request.TradeDate,
            Price: 0,
            Quantity: 1,
            RealizedPnl: null,
            RealizedPnlPct: null,
            CashAmount: request.Amount,
            CashAccountId: request.CashAccountId,
            Note: request.Note);

        return new TransactionWorkflowPlan(
            BuildTrades(mainTrade, request.Fee, request.CashAccountId, request.TradeDate,
                $"{request.Note} 手續費", null));
    }

    public TransactionWorkflowPlan CreateCashFlowPlan(CashFlowTransactionRequest request)
    {
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
            Note: request.Note);

        return new TransactionWorkflowPlan(
            BuildTrades(mainTrade, request.Fee, request.CashAccountId, request.TradeDate,
                $"{request.AccountName} 手續費", request.Note));
    }

    public TransactionWorkflowPlan CreateLoanPlan(LoanTransactionRequest request)
    {
        AssetItem? liabilityAsset = null;
        IReadOnlyList<LoanScheduleEntry>? scheduleEntries = null;

        if (request.Type == TradeType.LoanBorrow &&
            request.AmortAnnualRate.HasValue &&
            request.AmortTermMonths.HasValue &&
            request.FirstPaymentDate.HasValue)
        {
            liabilityAsset = new AssetItem(
                Guid.NewGuid(),
                request.LoanLabel,
                FinancialType.Liability,
                null,
                "TWD",
                DateOnly.FromDateTime(DateTime.Today),
                IsActive: true,
                UpdatedAt: null,
                LoanAnnualRate: request.AmortAnnualRate,
                LoanTermMonths: request.AmortTermMonths,
                LoanStartDate: request.FirstPaymentDate,
                LoanHandlingFee: request.Fee > 0 ? request.Fee : null);
            scheduleEntries = AmortizationService.Generate(
                liabilityAsset.Id,
                request.CashAmount,
                request.AmortAnnualRate.Value,
                request.AmortTermMonths.Value,
                request.FirstPaymentDate.Value);
        }

        var mainTrade = new Trade(
            Id: Guid.NewGuid(),
            Symbol: request.LoanLabel,
            Exchange: string.Empty,
            Name: request.LoanLabel,
            Type: request.Type,
            TradeDate: request.TradeDate,
            Price: 0,
            Quantity: 1,
            RealizedPnl: null,
            RealizedPnlPct: null,
            CashAmount: request.CashAmount,
            CashAccountId: request.CashAccountId,
            Note: request.Note,
            LoanLabel: request.LoanLabel,
            Principal: request.Principal,
            InterestPaid: request.InterestPaid);

        return new TransactionWorkflowPlan(
            BuildTrades(mainTrade, request.Fee, request.CashAccountId, request.TradeDate,
                $"{request.LoanLabel} 手續費", request.Note),
            liabilityAsset,
            scheduleEntries);
    }

    public TransactionWorkflowPlan CreateTransferPlan(TransferTransactionRequest request)
    {
        var trades = new List<Trade>();
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
            trades.Add(transfer);
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
            trades.Add(withdraw);
            feeParentId = withdraw.Id;

            var depositNote = string.IsNullOrWhiteSpace(request.Note)
                ? $"轉帳 ← {request.SourceName}"
                : $"轉帳 ← {request.SourceName} — {request.Note}";
            trades.Add(new Trade(
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
                Note: depositNote));
        }

        if (request.Fee > 0)
        {
            trades.Add(CreateFeeTrade(
                request.Fee,
                request.SourceCashAccountId,
                request.TradeDate,
                $"轉帳手續費 ({request.SourceName} → {request.DestinationName})",
                request.Note,
                feeParentId));
        }

        return new TransactionWorkflowPlan(trades);
    }

    private static IReadOnlyList<Trade> BuildTrades(
        Trade mainTrade,
        decimal fee,
        Guid? cashAccountId,
        DateTime tradeDate,
        string notePrefix,
        string? userNote)
    {
        var trades = new List<Trade> { mainTrade };
        if (fee > 0)
            trades.Add(CreateFeeTrade(fee, cashAccountId, tradeDate, notePrefix, userNote, mainTrade.Id));
        return trades;
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
