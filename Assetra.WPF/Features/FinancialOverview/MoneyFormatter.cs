using Assetra.Core.Models;

namespace Assetra.WPF.Features.FinancialOverview;

internal static class MoneyFormatter
{
    public static string Format(decimal amount, string currency)
    {
        return string.Equals(currency, "TWD", StringComparison.OrdinalIgnoreCase)
            ? $"NT${amount:N0}"
            : $"{currency} {amount:N0}";
    }

    /// <summary>
    /// <see cref="Money"/>-aware overload. Equivalent to
    /// <c>Format(money.Amount, money.Currency)</c> but keeps callers' currency
    /// context attached at the type system. M1 migration helper.
    /// </summary>
    public static string Format(Money money) => Format(money.Amount, money.Currency);
}
