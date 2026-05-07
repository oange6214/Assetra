using System.Globalization;
using System.Reflection;
using System.Windows.Data;

namespace Assetra.WPF.Infrastructure.Converters;

public sealed class DisplayOptionTextConverter : IValueConverter
{
    private static readonly string[] DisplayPropertyNames =
    [
        "DisplayName",
        "Display",
        "Label",
        "Name",
        "Title",
    ];

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null)
            return string.Empty;

        if (value is string text)
            return text;

        var type = value.GetType();
        foreach (var propertyName in DisplayPropertyNames)
        {
            var property = type.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
            if (property?.PropertyType != typeof(string))
                continue;

            if (property.GetValue(value) is string display && !string.IsNullOrWhiteSpace(display))
                return display;
        }

        return value.ToString() ?? string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
