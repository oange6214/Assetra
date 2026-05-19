using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Assetra.WPF.Infrastructure.Converters;

/// <summary>
/// Converts an integer count to <see cref="Visibility"/>.
/// Default: 0 → Visible, &gt;0 → Collapsed (used for empty-state placeholders).
/// With ConverterParameter="Inverse": &gt;0 → Visible, 0 → Collapsed
/// (used for hiding entire widgets when their collection is empty).
/// </summary>
[ValueConversion(typeof(int), typeof(Visibility))]
public sealed class ZeroToVisibleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not int n) return Visibility.Collapsed;
        var inverse = parameter is string s && string.Equals(s, "Inverse", StringComparison.OrdinalIgnoreCase);
        if (inverse)
            return n > 0 ? Visibility.Visible : Visibility.Collapsed;
        return n == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
