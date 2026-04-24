using System.Globalization;
using System.Windows.Data;

namespace Assetra.WPF.Infrastructure.Converters;

/// <summary>
/// Returns true when the effective width (actualWidth / uiScale) is below a threshold.
/// Useful for switching between table and card layouts under larger UI scale.
/// </summary>
public sealed class WidthScaleThresholdToBooleanConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        => Evaluate(values, parameter);

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    public static bool Evaluate(object[] values, object? parameter)
    {
        if (values.Length < 2)
            return false;

        if (!TryGetDouble(values[0], out var actualWidth) || actualWidth <= 0)
            return false;

        var uiScale = 1d;
        if (TryGetDouble(values[1], out var parsedScale) && parsedScale > 0)
            uiScale = parsedScale;

        if (!double.TryParse(parameter?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var threshold))
            threshold = 960d;

        var effectiveWidth = actualWidth / uiScale;
        return effectiveWidth < threshold;
    }

    private static bool TryGetDouble(object? value, out double result)
    {
        switch (value)
        {
            case double d:
                result = d;
                return true;
            case float f:
                result = f;
                return true;
            case decimal m:
                result = (double)m;
                return true;
            case int i:
                result = i;
                return true;
            case long l:
                result = l;
                return true;
            default:
                return double.TryParse(value?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out result);
        }
    }
}
