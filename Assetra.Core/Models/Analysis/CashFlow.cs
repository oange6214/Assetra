namespace Assetra.Core.Models.Analysis;

/// <summary>
/// Cash flow used by IRR / XIRR / MWR.
/// <para>
/// <see cref="Currency"/> is optional — when null/empty the amount is assumed to already be in
/// the caller's base currency. Multi-currency callers tag each flow with its source currency
/// and run them through <c>MultiCurrencyCashFlowConverter</c> before passing to XIRR.
/// </para>
/// </summary>
public sealed record CashFlow(DateOnly Date, decimal Amount, string? Currency = null);
