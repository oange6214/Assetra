namespace Assetra.Core.Models;

/// <summary>
/// Currency value object. <see cref="Code"/> is ISO 4217 (e.g. "TWD", "USD").
/// </summary>
public sealed record Currency(string Code, string Symbol, int DecimalPlaces)
{
    public static readonly Currency TWD = new("TWD", "NT$", 0);
    public static readonly Currency USD = new("USD", "$", 2);
    public static readonly Currency JPY = new("JPY", "¥", 0);
    public static readonly Currency HKD = new("HKD", "HK$", 2);
    public static readonly Currency EUR = new("EUR", "€", 2);
    public static readonly Currency CNY = new("CNY", "¥", 2);
    public static readonly Currency GBP = new("GBP", "£", 2);

    public static IReadOnlyList<Currency> Known { get; } =
        [TWD, USD, JPY, HKD, EUR, CNY, GBP];

    /// <summary>Returns the registered <see cref="Currency"/> for <paramref name="code"/>, or a synthesized placeholder if unknown.</summary>
    public static Currency FromCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Currency code is required.", nameof(code));
        var upper = code.Trim().ToUpperInvariant();
        foreach (var c in Known)
            if (string.Equals(c.Code, upper, StringComparison.Ordinal))
                return c;
        return new Currency(upper, upper, 2);
    }
}
