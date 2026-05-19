namespace Assetra.Core.Models;

public static class CurrencyValuation
{
    public static decimal ConvertToBase(
        decimal amount,
        string? fromCurrency,
        string? baseCurrency,
        IReadOnlyDictionary<string, decimal>? ratesToTwd)
    {
        var from = NormalizeCurrency(fromCurrency);
        var target = NormalizeCurrency(baseCurrency);
        if (string.Equals(from, target, StringComparison.Ordinal))
            return amount;

        var twd = string.Equals(from, "TWD", StringComparison.Ordinal)
            ? amount
            : TryGetRate(ratesToTwd, from, out var fromRate)
                ? amount * fromRate
                : amount;

        if (string.Equals(target, "TWD", StringComparison.Ordinal))
            return twd;

        return TryGetRate(ratesToTwd, target, out var targetRate)
            ? twd / targetRate
            : twd;
    }

    private static string NormalizeCurrency(string? currency) =>
        string.IsNullOrWhiteSpace(currency)
            ? "TWD"
            : currency.Trim().ToUpperInvariant();

    private static bool TryGetRate(
        IReadOnlyDictionary<string, decimal>? rates,
        string currency,
        out decimal rate)
    {
        rate = 0m;
        return rates is not null &&
               rates.TryGetValue(currency, out rate) &&
               rate > 0m;
    }
}
