using System.Globalization;
using System.Windows.Data;

namespace Assetra.WPF.Infrastructure.Converters;

/// <summary>
/// IMultiValueConverter: [containerWidth, uiScale] -> clamped panel width.
/// Parameter format: "fraction|min|max", e.g. "0.46|420|620".
/// The width is computed in logical units so larger UiScale reduces usable width.
/// </summary>
public sealed class AdaptivePanelWidthConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 1)
            return 520d;

        var containerWidth = values[0] switch
        {
            double d => d,
            float f => (double)f,
            int i => i,
            _ => 0d,
        };

        var uiScale = values.Length > 1
            ? values[1] switch
            {
                double d when d > 0 => d,
                float f when f > 0 => f,
                decimal m when m > 0 => (double)m,
                _ => 1d,
            }
            : 1d;

        var fraction = 0.46d;
        var min = 420d;
        var max = 620d;

        if (parameter is string s && !string.IsNullOrWhiteSpace(s))
        {
            var parts = s.Split('|');
            if (parts.Length > 0 && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedFraction))
                fraction = parsedFraction;
            if (parts.Length > 1 && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedMin))
                min = parsedMin;
            if (parts.Length > 2 && double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedMax))
                max = parsedMax;
        }

        var logicalWidth = containerWidth / Math.Max(uiScale, 0.01d);
        var desiredWidth = logicalWidth * fraction;
        return Math.Clamp(desiredWidth, min, max);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
