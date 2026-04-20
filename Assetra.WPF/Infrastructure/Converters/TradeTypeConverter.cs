using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Assetra.Core.Models;

namespace Assetra.WPF.Infrastructure.Converters;

/// <summary>
/// Converts a <see cref="TradeType"/> enum to a localized display string
/// by looking up the resource key "Portfolio.TradeType.{EnumName}" at runtime.
/// Falls back to the enum name if the resource key is not found.
/// </summary>
public sealed class TradeTypeConverter : IValueConverter
{
    private static readonly IReadOnlyDictionary<TradeType, string> Keys =
        new Dictionary<TradeType, string>
        {
            [TradeType.Buy] = "Portfolio.TradeType.Buy",
            [TradeType.Sell] = "Portfolio.TradeType.Sell",
            [TradeType.Income] = "Portfolio.TradeType.Income",
            [TradeType.CashDividend] = "Portfolio.TradeType.CashDividend",
            [TradeType.StockDividend] = "Portfolio.TradeType.StockDividend",
            [TradeType.Deposit] = "Portfolio.TradeType.Deposit",
            [TradeType.Withdrawal] = "Portfolio.TradeType.Withdrawal",
            [TradeType.LoanBorrow] = "Portfolio.TradeType.LoanBorrow",
            [TradeType.LoanRepay] = "Portfolio.TradeType.LoanRepay",
        };

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not TradeType type)
            return value?.ToString() ?? string.Empty;

        if (!Keys.TryGetValue(type, out var key))
            return type.ToString();

        return Application.Current.TryFindResource(key) as string ?? type.ToString();
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
