using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Assetra.WPF.Infrastructure.Converters;

/// <summary>
/// Resolves a resource-key string (e.g. "Analysis.Financials.Row.Revenue")
/// to its localized value from the active language ResourceDictionary.
/// Used by row-based controls whose content is driven by a data-bound key
/// rather than a compile-time known DynamicResource reference.
/// </summary>
public sealed class ResourceKeyToStringConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string key || string.IsNullOrWhiteSpace(key))
            return string.Empty;
        return Application.Current?.TryFindResource(key) as string ?? key;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
