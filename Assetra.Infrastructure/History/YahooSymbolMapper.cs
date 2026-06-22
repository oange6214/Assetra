namespace Assetra.Infrastructure.History;

/// <summary>
/// Maps an Assetra <c>(symbol, exchange)</c> pair to the Yahoo Finance ticker convention.
/// Pure function — no I/O.
/// <list type="bullet">
///   <item>TWSE → <c>{symbol}.TW</c></item>
///   <item>TPEX → <c>{symbol}.TWO</c></item>
///   <item>US venues → <c>{symbol}</c> (bare)</item>
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
        // 指數（^TWII / ^GSPC）：Yahoo 用原樣、不加交易所後綴。
        if (s.StartsWith('^'))
            return s;
        var ex = exchange?.Trim().ToUpperInvariant();
        return ex switch
        {
            // "TWSE"/"TPEX" 是內部交易所碼；"TW"/"TWO" 是比較 token 拆出來的後綴
            // （如 0050.TW → ("0050","TW")）。兩種都要對應到 Yahoo 的 .TW / .TWO。
            "TWSE" or "TW" => $"{s}.TW",
            "TPEX" or "TWO" => $"{s}.TWO",
            "HKEX" => $"{s}.HK",
            "TSE" => $"{s}.T",
            "NYSE" or "NASDAQ" or "NYSEARCA" or "AMEX" or "BATS" or "IEX" => s,
            _ => s,
        };
    }

    /// <summary>True for non-Taiwan venues that Twse/Tpex/FinMind providers cannot serve.</summary>
    public static bool IsForeignExchange(string? exchange)
    {
        var ex = exchange?.Trim().ToUpperInvariant();
        return ex is "NYSE" or "NASDAQ" or "NYSEARCA" or "AMEX" or "BATS" or "IEX" or "HKEX" or "TSE";
    }
}
