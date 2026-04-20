using System.Globalization;
using System.Windows.Data;

namespace Assetra.WPF.Infrastructure.Converters;

/// <summary>
/// Two-way string equality converter for RadioButton filter groups.
/// Convert:     value == parameter → true (IsChecked)
/// ConvertBack: true → parameter value; false → Binding.DoNothing
/// </summary>
[ValueConversion(typeof(string), typeof(bool))]
public sealed class StringEqualityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is string s && parameter is string p && s == p;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? parameter : Binding.DoNothing;
}
