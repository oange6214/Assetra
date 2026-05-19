namespace Assetra.Core.Models;

public static class EquitySymbolNormalizer
{
    public static string NormalizeCanonicalSymbol(string? symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            return string.Empty;

        var normalized = symbol.Trim()
            .ToUpperInvariant()
            .Replace('$', '.')
            .Replace(" ", string.Empty, StringComparison.Ordinal);

        while (normalized.Contains("..", StringComparison.Ordinal))
            normalized = normalized.Replace("..", ".", StringComparison.Ordinal);

        return normalized.Trim('.');
    }

    public static string ToSearchKey(string? symbol)
    {
        var canonical = NormalizeCanonicalSymbol(symbol);
        return canonical.Replace(".", string.Empty, StringComparison.Ordinal);
    }

    public static bool SymbolMatches(string? candidate, string? query) =>
        string.Equals(ToSearchKey(candidate), ToSearchKey(query), StringComparison.OrdinalIgnoreCase);

    public static string NormalizeExchange(string? exchange)
    {
        if (string.IsNullOrWhiteSpace(exchange))
            return string.Empty;

        var normalized = exchange.Trim()
            .ToUpperInvariant()
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal);

        return normalized switch
        {
            "XTAI" => "TWSE",
            "ROCO" => "TPEX",
            "XNAS" => "NASDAQ",
            "XNYS" => "NYSE",
            "XASE" => "AMEX",
            "NYSEAMERICAN" => "AMEX",
            "NYSEMKT" => "AMEX",
            "ARCX" => "NYSEARCA",
            "NYSEARCA" => "NYSEARCA",
            _ => normalized,
        };
    }
}
