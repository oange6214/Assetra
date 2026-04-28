namespace Assetra.Infrastructure.History;

/// <summary>
/// Maps an Assetra <c>(symbol, exchange)</c> pair to the Yahoo Finance ticker convention.
/// Pure function — no I/O.
/// <list type="bullet">
///   <item>TWSE → <c>{symbol}.TW</c></item>
///   <item>TPEX → <c>{symbol}.TWO</c></item>
///   <item>NYSE / NASDAQ / AMEX → <c>{symbol}</c> (bare)</item>
///   <item>HKEX → <c>{symbol}.HK</c></item>
///   <item>TSE  → <c>{symbol}.T</c></item>
///   <item>unknown → bare symbol (caller-validated)</item>
/// </list>
/// </summary>
public static class YahooSymbolMapper
{
    public static string ToYahooSymbol(string symbol, string? exchange)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        var s = symbol.Trim();
        var ex = exchange?.Trim().ToUpperInvariant();
        return ex switch
        {
            "TWSE" => $"{s}.TW",
            "TPEX" => $"{s}.TWO",
            "HKEX" => $"{s}.HK",
            "TSE" => $"{s}.T",
            "NYSE" or "NASDAQ" or "AMEX" => s,
            _ => s,
        };
    }

    /// <summary>True for non-Taiwan venues that Twse/Tpex/FinMind providers cannot serve.</summary>
    public static bool IsForeignExchange(string? exchange)
    {
        var ex = exchange?.Trim().ToUpperInvariant();
        return ex is "NYSE" or "NASDAQ" or "AMEX" or "HKEX" or "TSE";
    }
}
