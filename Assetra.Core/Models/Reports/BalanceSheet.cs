namespace Assetra.Core.Models.Reports;

public sealed record BalanceSheet(
    DateOnly AsOf,
    StatementSection Assets,
    StatementSection Liabilities,
    decimal NetWorth,
    /// <summary>
    /// User-facing warnings produced while building this statement (e.g.
    /// missing FX rates for currencies that hold positions). Empty list =
    /// no warnings. Each string is already localized — surface verbatim
    /// in a banner so silent fx-fallback no longer looks like data corruption.
    /// </summary>
    IReadOnlyList<string>? Warnings = null);
