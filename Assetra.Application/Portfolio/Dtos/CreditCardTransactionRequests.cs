using Assetra.Core.Models;

namespace Assetra.Application.Portfolio.Dtos;

public sealed record CreditCardChargeRequest(
    Guid CreditCardAssetId,
    string CardName,
    DateTime TradeDate,
    decimal Amount,
    string? Note);

public sealed record CreditCardPaymentRequest(
    Guid CreditCardAssetId,
    string CardName,
    Guid CashAccountId,
    string CashAccountName,
    DateTime TradeDate,
    decimal Amount,
    string? Note);

public sealed record CreditCardTransactionResult(Trade Trade);
