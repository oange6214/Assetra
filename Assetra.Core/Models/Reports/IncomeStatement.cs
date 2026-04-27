namespace Assetra.Core.Models.Reports;

public sealed record IncomeStatement(
    ReportPeriod Period,
    StatementSection Income,
    StatementSection Expense,
    decimal Net,
    IncomeStatement? Prior = null);
