namespace Assetra.Core.Models.Reports;

public sealed record CashFlowStatement(
    ReportPeriod Period,
    StatementSection Operating,
    StatementSection Investing,
    StatementSection Financing,
    decimal NetChange,
    decimal OpeningCash,
    decimal ClosingCash);
