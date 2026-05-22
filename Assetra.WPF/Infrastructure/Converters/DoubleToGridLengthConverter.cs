using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Assetra.WPF.Infrastructure.Converters;

/// <summary>
/// P4.9g — Converts a <see cref="double"/> pixel width into a <see cref="GridLength"/>
/// for binding to <c>ColumnDefinition.Width</c> / <c>RowDefinition.Height</c>.
/// WPF doesn't auto-coerce double → GridLength for bindings (XAML literal
/// coercion only). Use when one part of the UI must size-match an animated
/// width elsewhere in the tree, e.g., MainWindow side-panel backdrops aligning
/// to the current <c>NavRailView.NavPaneWidth</c>.
/// </summary>
[ValueConversion(typeof(double), typeof(GridLength))]
public sealed class DoubleToGridLengthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double d && !double.IsNaN(d) && d >= 0)
            return new GridLength(d);
        return GridLength.Auto;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
