using Assetra.AppLayer.Portfolio.Contracts;
using Assetra.AppLayer.Portfolio.Dtos;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;

namespace Assetra.AppLayer.Portfolio.Services;

public sealed class LoanPaymentWorkflowService : ILoanPaymentWorkflowService
{
    private readonly ITradeRepository _tradeRepository;
    private readonly ILoanScheduleRepository _loanScheduleRepository;

    public LoanPaymentWorkflowService(
        ITradeRepository tradeRepository,
        ILoanScheduleRepository loanScheduleRepository)
    {
        _tradeRepository = tradeRepository;
        _loanScheduleRepository = loanScheduleRepository;
    }

    public async Task<LoanPaymentResult> RecordAsync(
        LoanPaymentRequest request,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var total = request.Entry.PrincipalAmount + request.Entry.InterestAmount;
        var repayTrade = new Trade(
            Id: Guid.NewGuid(),
            Symbol: string.Empty,
            Exchange: string.Empty,
            Name: request.LoanLabel,
            Type: TradeType.LoanRepay,
            TradeDate: request.TradeDate,
            Price: total,
            Quantity: 1,
            RealizedPnl: 0m,
            RealizedPnlPct: 0m,
            CashAmount: total,
            CashAccountId: request.CashAccountId,
            LoanLabel: request.LoanLabel,
            Principal: request.Entry.PrincipalAmount,
            InterestPaid: request.Entry.InterestAmount);

        await _tradeRepository.AddAsync(repayTrade).ConfigureAwait(false);
        var paidAt = DateTime.UtcNow;
        await _loanScheduleRepository
            .MarkPaidAsync(request.Entry.Id, paidAt, repayTrade.Id)
            .ConfigureAwait(false);

        return new LoanPaymentResult(repayTrade, paidAt);
    }
}
