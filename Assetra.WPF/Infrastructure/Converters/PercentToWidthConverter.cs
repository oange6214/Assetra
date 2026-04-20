using System.Globalization;
using System.Windows.Data;

namespace Assetra.WPF.Infrastructure.Converters;

/// <summary>
/// IMultiValueConverter: [percentValue (decimal 0–100), containerWidth (double)] → pixel width (double).
/// Used to render thin progress bars without a custom ProgressBar template.
/// </summary>
public sealed class PercentToWidthConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2)
            return 0d;

        var pct = values[0] switch
        {
            decimal d => (double)d,
            double d => d,
            float f => (double)f,
            _ => 0d,
        };

        var containerWidth = values[1] switch
        {
            double d => d,
            float f => (double)f,
            _ => 0d,
        };

        pct = Math.Clamp(pct, 0d, 100d);
        return containerWidth * pct / 100d;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
