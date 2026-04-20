using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Assetra.WPF.Infrastructure.Converters;

/// <summary>Converts a hex color string (e.g. "#3B82F6") to a SolidColorBrush.</summary>
[ValueConversion(typeof(string), typeof(SolidColorBrush))]
public sealed class HexToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string hex && hex.Length >= 4)
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(hex);
                return new SolidColorBrush(color);
            }
            catch (FormatException) { /* malformed hex — fall through to default brush */ }
        }
        return Brushes.Gray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
