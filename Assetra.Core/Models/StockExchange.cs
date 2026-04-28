namespace Assetra.Core.Models;

/// <summary>
/// Metadata for a stock exchange / trading venue.
/// </summary>
/// <param name="Code">Canonical short code, e.g. "TWSE", "NASDAQ", "HKEX".</param>
/// <param name="DisplayName">Human-readable name, e.g. "Taiwan Stock Exchange".</param>
/// <param name="DefaultCurrency">ISO 4217 currency listings on this venue typically settle in.</param>
/// <param name="Country">ISO 3166-1 alpha-2 country code, e.g. "TW", "US".</param>
/// <param name="TimeZone">IANA tzdata identifier for trading hours, e.g. "Asia/Taipei", "America/New_York".</param>
public sealed record StockExchange(
    string Code,
    string DisplayName,
    string DefaultCurrency,
    string Country,
    string TimeZone);
