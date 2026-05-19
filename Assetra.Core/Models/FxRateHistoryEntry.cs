namespace Assetra.Core.Models;

/// <summary>
/// One historical FX quote — used by multi-currency reports to convert
/// per-position values into a chosen base currency as-of a past date.
/// PK = (Date, BaseCurrency, QuoteCurrency).
/// </summary>
/// <param name="Date">The trading date this rate applies to (UTC date).</param>
/// <param name="BaseCurrency">From-side (ISO 4217, e.g. "USD").</param>
/// <param name="QuoteCurrency">To-side (e.g. "TWD").</param>
/// <param name="Rate"><c>1 BaseCurrency = Rate QuoteCurrency</c>. Always positive.</param>
/// <param name="Source">
/// Where the rate came from (e.g. "yahoo", "ecb", "manual"). Lets us audit
/// + switch providers without losing historical data.
/// </param>
/// <param name="IngestedAt">UTC timestamp when this row was written.</param>
public sealed record FxRateHistoryEntry(
    DateOnly Date,
    string BaseCurrency,
    string QuoteCurrency,
    decimal Rate,
    string Source,
    DateTimeOffset IngestedAt);
