using Assetra.Core.Models;

namespace Assetra.AppLayer.Portfolio.Dtos;

public sealed record TransactionWorkflowPlan(
    IReadOnlyList<Trade> Trades,
    AssetItem? LiabilityAsset = null,
    IReadOnlyList<LoanScheduleEntry>? LoanScheduleEntries = null);

public sealed record IncomeTransactionRequest(
    decimal Amount,
    DateTime TradeDate,
    Guid? CashAccountId,
    string Note,
    decimal Fee);

public sealed record CashFlowTransactionRequest(
    TradeType Type,
    decimal Amount,
    DateTime TradeDate,
    Guid CashAccountId,
    string AccountName,
    string? Note,
    decimal Fee);

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
