using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Assetra.WPF.Infrastructure.Converters;

/// <summary>
/// 將 decimal 值轉為 Visibility：&gt; 0 → Visible；&lt;= 0 或 null → Collapsed。
/// 用於 AMT 公式分解 — 只顯示「有實際計入」的加項列。
/// </summary>
[ValueConversion(typeof(decimal), typeof(Visibility))]
public sealed class NonZeroDecimalToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        decimal d = value switch
        {
            decimal dv => dv,
            double dbl => (decimal)dbl,
            int i => i,
            _ => 0m,
        };
        return d > 0m ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
