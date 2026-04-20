using System.Globalization;

namespace Assetra.Infrastructure.Http;

internal static class MisParsing
{
    public static bool IsValidPrice(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value != "-" && value != "--";

    public static decimal ParseDecimal(string? value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var r) ? r : 0m;

    public static long ParseLong(string? value) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var r) ? r : 0L;

    public static int ParseInt(string? value) =>
        int.TryParse(value?.Replace(",", ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out var r) ? r : 0;
}
