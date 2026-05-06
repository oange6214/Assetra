using System.Globalization;
using System.Windows.Data;

namespace Assetra.WPF.Infrastructure.Converters;

[ValueConversion(typeof(DateTime), typeof(string))]
public sealed class DateDisplayConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var format = parameter?.ToString();
        if (string.IsNullOrWhiteSpace(format))
            format = "yyyy-MM-dd";

        return value switch
        {
            null => "-",
            DateTime date => date.ToString(format, CultureInfo.InvariantCulture),
            DateTimeOffset date => date.LocalDateTime.ToString(format, CultureInfo.InvariantCulture),
            DateOnly date => date.ToString(format, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty,
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
