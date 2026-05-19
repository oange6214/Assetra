using Assetra.Core.Models;

namespace Assetra.Application.Portfolio.Dtos;

/// <summary>
/// Income transaction. <paramref name="AccountName"/> is what gets displayed in the
/// trade list "資產" column (mirrors the Deposit/Withdrawal convention); pass an
/// empty string when no cash account is linked, the column will then render as "—".
/// <paramref name="Note"/> is the free-text user note (e.g. "6 月薪水"), separate
/// from <paramref name="CategoryId"/> so editing the category never mutates the
/// note nor the asset display.
/// </summary>
public sealed record IncomeTransactionRequest(
    decimal Amount,
    DateTime TradeDate,
    Guid? CashAccountId,
    string AccountName,
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
    decimal Fee,
    // MultiCurrency-Trade-Refactor P3 — 跨幣別股息：標的計 USD 股息、入款 TWD 帳戶。
    // ActualCashAmount = 券商實際入帳（帳戶幣別）；FxRate = 標的→帳戶匯率。
    // 同幣別股息兩者皆 null。
    decimal? ActualCashAmount = null,
    decimal? FxRate = null,
    // Portfolio-Groups-Refactor P3
    Guid? PortfolioGroupId = null);

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
    DateOnly? FirstPaymentDate = null,
    string? Subtype = null);

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
