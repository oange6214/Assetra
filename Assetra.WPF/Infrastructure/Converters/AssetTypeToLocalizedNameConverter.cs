using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Assetra.Core.Models;

namespace Assetra.WPF.Infrastructure.Converters;

/// <summary>
/// Maps an <see cref="AssetType"/> enum value to its localized display name by
/// looking up the language ResourceDictionary key <c>Portfolio.Filter.AssetType.{Name}</c>
/// (the same keys used by the position filter chips, so we don't duplicate strings).
/// Falls back to the enum's <c>ToString()</c> if the resource is missing.
/// </summary>
public sealed class AssetTypeToLocalizedNameConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not AssetType at) return string.Empty;
        var key = $"Portfolio.Filter.AssetType.{at}";
        return System.Windows.Application.Current?.TryFindResource(key) as string ?? at.ToString();
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
