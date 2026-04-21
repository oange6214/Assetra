using Assetra.Core.Models;

namespace Assetra.Application.Portfolio.Dtos;

public sealed record LoanPaymentRequest(
    LoanScheduleEntry Entry,
    string LoanLabel,
    Guid? CashAccountId,
    DateTime TradeDate);

public sealed record LoanPaymentResult(
    Trade RepayTrade,
    DateTime PaidAt);
