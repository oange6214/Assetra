using System.Globalization;

namespace Assetra.WPF.Infrastructure;

/// <summary>
/// Culture-invariant parsing helpers used across ViewModels for user-entered numeric text.
/// Centralises the <c>NumberStyles.Any</c> + <c>CultureInfo.InvariantCulture</c> idiom so
/// every screen accepts the same input conventions (e.g. thousand separators, decimal point).
/// </summary>
internal static class ParseHelpers
{
    /// <summary>
    /// Parses <paramref name="s"/> as a decimal using invariant culture and
    /// <see cref="NumberStyles.Any"/>. Returns <c>false</c> on null / whitespace / invalid input.
    /// </summary>
    public static bool TryParseDecimal(string? s, out decimal value)
    {
        if (string.IsNullOrWhiteSpace(s))
        {
            value = 0m;
            return false;
        }
        return decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>
    /// Fluent variant: returns the parsed value or <c>null</c> on failure.
    /// </summary>
    public static decimal? ParseDecimalOrNull(string? s) =>
        TryParseDecimal(s, out var v) ? v : null;
}
