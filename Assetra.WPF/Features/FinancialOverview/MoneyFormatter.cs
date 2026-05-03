namespace Assetra.WPF.Features.FinancialOverview;

internal static class MoneyFormatter
{
    public static string Format(decimal amount, string currency)
    {
        return string.Equals(currency, "TWD", StringComparison.OrdinalIgnoreCase)
            ? $"NT${amount:N0}"
            : $"{currency} {amount:N0}";
    }
}
