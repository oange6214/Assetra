using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Assetra.WPF.Infrastructure.Converters;

/// <summary>
/// Returns Visible when the effective width (actualWidth / uiScale) is below a threshold.
/// Useful for compact layouts under larger UI scale.
/// </summary>
public sealed class WidthScaleThresholdToVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var isCompact = WidthScaleThresholdToBooleanConverter.Evaluate(values, parameter);
        return isCompact ? Visibility.Visible : Visibility.Collapsed;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
