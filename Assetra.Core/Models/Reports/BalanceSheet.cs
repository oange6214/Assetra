namespace Assetra.Core.Models.Reports;

public sealed record BalanceSheet(
    DateOnly AsOf,
    StatementSection Assets,
    StatementSection Liabilities,
    decimal NetWorth);
