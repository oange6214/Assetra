using Assetra.Core.Models;

namespace Assetra.Application.Portfolio.Dtos;

public sealed record IncomeTransactionRequest(
    decimal Amount,
    DateTime TradeDate,
    Guid? CashAccountId,
    string Note,
    decimal Fee,
    Guid? CategoryId = null);

public sealed record CashDividendTransactionRequest(
    string Symbol,
    string Exchange,
    string Name,
    decimal PerShare,
    int Quantity,
    decimal TotalAmount,
    DateTime TradeDate,
    Guid? CashAccountId,
    decimal Fee);

public sealed record StockDividendTransactionRequest(
    string Symbol,
    string Exchange,
    string Name,
    int NewShares,
    DateTime TradeDate,
    Guid PortfolioEntryId);

public sealed record CashFlowTransactionRequest(
    TradeType Type,
    decimal Amount,
    DateTime TradeDate,
    Guid CashAccountId,
    string AccountName,
    string? Note,
    decimal Fee,
    Guid? CategoryId = null);

public sealed record LoanTransactionRequest(
    TradeType Type,
    decimal CashAmount,
    DateTime TradeDate,
    string LoanLabel,
    Guid? CashAccountId,
    string? Note,
    decimal Fee,
    decimal? Principal = null,
    decimal? InterestPaid = null,
    decimal? AmortAnnualRate = null,
    int? AmortTermMonths = null,
    DateOnly? FirstPaymentDate = null);

public sealed record TransferTransactionRequest(
    Guid SourceCashAccountId,
    string SourceName,
    Guid DestinationCashAccountId,
    string DestinationName,
    decimal SourceAmount,
    decimal DestinationAmount,
    DateTime TradeDate,
    string? Note,
    decimal Fee);
