namespace Assetra.Core.Models;

/// <summary>
/// Immutable registry of supported stock exchanges with default currency / region metadata.
/// Lookups are case-insensitive on the exchange code. Codes follow the canonical convention
/// already used elsewhere in the codebase (TWSE / TPEX / NYSE / NASDAQ / AMEX / HKEX / TSE).
/// </summary>
public static class StockExchangeRegistry
{
    public static readonly StockExchange TWSE = new("TWSE", "Taiwan Stock Exchange", "TWD", "TW", "Asia/Taipei");
    public static readonly StockExchange TPEX = new("TPEX", "Taipei Exchange", "TWD", "TW", "Asia/Taipei");
    public static readonly StockExchange NYSE = new("NYSE", "New York Stock Exchange", "USD", "US", "America/New_York");
    public static readonly StockExchange NASDAQ = new("NASDAQ", "Nasdaq Stock Market", "USD", "US", "America/New_York");
    public static readonly StockExchange AMEX = new("AMEX", "NYSE American", "USD", "US", "America/New_York");
    public static readonly StockExchange HKEX = new("HKEX", "Hong Kong Exchanges", "HKD", "HK", "Asia/Hong_Kong");
    public static readonly StockExchange TSE = new("TSE", "Tokyo Stock Exchange", "JPY", "JP", "Asia/Tokyo");

    public static IReadOnlyList<StockExchange> Known { get; } = [TWSE, TPEX, NYSE, NASDAQ, AMEX, HKEX, TSE];

    private static readonly Dictionary<string, StockExchange> Map =
        Known.ToDictionary(e => e.Code, StringComparer.OrdinalIgnoreCase);

    /// <summary>Look up an exchange by code (case-insensitive). Returns null if unknown.</summary>
    public static StockExchange? TryGet(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        return Map.TryGetValue(code.Trim(), out var ex) ? ex : null;
    }

    /// <summary>
    /// Default settlement currency for the given exchange code.
    /// Falls back to <paramref name="fallback"/> (default "TWD") if the exchange is unknown.
    /// </summary>
    public static string ResolveDefaultCurrency(string? code, string fallback = "TWD")
        => TryGet(code)?.DefaultCurrency ?? fallback;
}
