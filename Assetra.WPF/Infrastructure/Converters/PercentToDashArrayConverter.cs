using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Assetra.WPF.Infrastructure.Converters;

/// <summary>
/// P5.10 — Converts a 0–100 percentage to a <see cref="DoubleCollection"/> usable as
/// <c>Ellipse.StrokeDashArray</c> so that a stroked ellipse renders only the leading
/// portion (donut-style progress indicator). Combined with <c>RenderTransform</c>
/// (e.g., RotateTransform −90°) the start point can be moved to the 12 o'clock position.
/// <para>
/// <c>ConverterParameter</c> is the total dash units of the ellipse's circumference
/// expressed in <c>StrokeThickness</c> multiples (WPF stroke-dash units). For an
/// ellipse with radius 18 (diameter 36) and stroke 4, the circumference is
/// 2π·18 ≈ 113.1 pixels which is 113.1 / 4 ≈ 28.3 stroke-dash units. Pass "28.3"
/// (or the matching value for your ellipse + stroke).
/// </para>
/// </summary>
[ValueConversion(typeof(double), typeof(DoubleCollection))]
public sealed class PercentToDashArrayConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var totalDashUnits = parameter switch
        {
            string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var p) => p,
            double d => d,
            _ => 28.3,
        };

        var pct = value switch
        {
            double d => d,
            decimal dec => (double)dec,
            float f => f,
            int i => i,
            _ => 0.0,
        };

        pct = Math.Clamp(pct, 0, 100);
        var filled = totalDashUnits * pct / 100.0;
        var gap = Math.Max(0, totalDashUnits - filled);
        return new DoubleCollection { filled, gap };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
