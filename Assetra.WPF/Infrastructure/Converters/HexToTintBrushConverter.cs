using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Assetra.WPF.Infrastructure.Converters;

/// <summary>
/// Converts a hex color string (e.g. "#3B82F6") to a low-opacity SolidColorBrush
/// suitable for tinted backgrounds behind a tinted Fluent icon — gives the same
/// "soft pill" look used by the navrail / dialog accent badges.
/// </summary>
[ValueConversion(typeof(string), typeof(SolidColorBrush))]
public sealed class HexToTintBrushConverter : IValueConverter
{
    /// <summary>Alpha applied to the source color. ~14% feels close to navrail tints.</summary>
    private const byte TintAlpha = 0x24;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string hex && hex.Length >= 4)
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(hex);
                return new SolidColorBrush(Color.FromArgb(TintAlpha, color.R, color.G, color.B));
            }
            catch (FormatException) { /* malformed hex — fall through */ }
        }
        return new SolidColorBrush(Color.FromArgb(TintAlpha, 0x6B, 0x72, 0x80));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
