using System.Globalization;
using System.Windows.Data;

namespace Assetra.WPF.Infrastructure.Converters;

/// <summary>
/// IValueConverter: input double minus ConverterParameter (double, also accepts string).
/// Negative results clamped to 0.
///
/// Use case: bind a dialog's <c>MaxHeight</c> to the parent Window's <c>ActualHeight</c>
/// minus a margin so the dialog can grow with the window but always leave breathing
/// space at top + bottom.
/// </summary>
public sealed class SubtractDoubleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var input = value switch
        {
            double d => d,
            float f => (double)f,
            int i => (double)i,
            _ => 0d,
        };
        var offset = parameter switch
        {
            double d => d,
            float f => (double)f,
            int i => (double)i,
            string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var p) => p,
            _ => 0d,
        };
        return Math.Max(0d, input - offset);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
