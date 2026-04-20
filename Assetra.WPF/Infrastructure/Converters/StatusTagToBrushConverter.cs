using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Assetra.WPF.Infrastructure.Converters;

/// <summary>
/// Converts a status tag string ("safe"/"healthy"/"warning"/"danger"/"none")
/// to a SolidColorBrush. Colors are hardcoded to avoid unreliable
/// Application.TryFindResource lookups across theme swaps.
/// </summary>
[ValueConversion(typeof(string), typeof(Brush))]
public sealed class StatusTagToBrushConverter : IValueConverter
{
    // Frozen brushes — allocated once, never mutated
    private static readonly SolidColorBrush BrushUp = Make("#0CBB8A"); // AppUp   (dark)
    private static readonly SolidColorBrush BrushWarning = Make("#F59E0B"); // amber
    private static readonly SolidColorBrush BrushDown = Make("#F24B4B"); // AppDown (dark)
    private static readonly SolidColorBrush BrushMuted = Make("#5E5E5E"); // AppTextMuted (dark)

    private static SolidColorBrush Make(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        (value as string) switch
        {
            "safe" or "healthy" => BrushUp,
            "warning" => BrushWarning,
            "danger" => BrushDown,
            _ => BrushMuted,
        };


    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
