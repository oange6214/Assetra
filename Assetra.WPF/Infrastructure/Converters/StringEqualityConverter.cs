using System.Globalization;
using System.Windows.Data;

namespace Assetra.WPF.Infrastructure.Converters;

/// <summary>
/// String equality converter.
/// As <see cref="IValueConverter"/>: value == parameter → true (e.g. RadioButton groups).
/// As <see cref="IMultiValueConverter"/>: values[0] == values[1] → true
/// (e.g. comparing an element's Tag to a VM property in a DataTrigger).
/// </summary>
[ValueConversion(typeof(string), typeof(bool))]
public sealed class StringEqualityConverter : IValueConverter, IMultiValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is string s && parameter is string p && s == p;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? parameter : Binding.DoNothing;

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        => values is { Length: >= 2 }
           && values[0]?.ToString() == values[1]?.ToString();

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
