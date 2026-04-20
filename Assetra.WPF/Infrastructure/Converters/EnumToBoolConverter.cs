using System.Globalization;
using System.Windows.Data;

namespace Assetra.WPF.Infrastructure.Converters;

public sealed class EnumToBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value?.ToString() == parameter?.ToString();

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not true)
            return Binding.DoNothing;

        // String target (e.g. SelectedExchange bound as string) — return the parameter directly
        if (!targetType.IsEnum)
            return parameter?.ToString() ?? string.Empty;

        // Enum target — parse the parameter string into the correct enum value
        if (parameter is string p)
            return Enum.Parse(targetType, p);

        return Binding.DoNothing;
    }
}
