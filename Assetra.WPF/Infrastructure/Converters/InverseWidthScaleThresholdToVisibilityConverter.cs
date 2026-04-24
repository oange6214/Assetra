using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Assetra.WPF.Infrastructure.Converters;

/// <summary>
/// Returns Visible when the effective width (actualWidth / uiScale) is above or equal to a threshold.
/// Useful for full layouts under larger UI scale.
/// </summary>
public sealed class InverseWidthScaleThresholdToVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var isCompact = WidthScaleThresholdToBooleanConverter.Evaluate(values, parameter);
        return isCompact ? Visibility.Collapsed : Visibility.Visible;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
