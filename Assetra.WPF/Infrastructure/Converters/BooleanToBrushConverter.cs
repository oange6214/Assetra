using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Assetra.WPF.Infrastructure.Converters;

[ValueConversion(typeof(bool), typeof(Brush))]
public sealed class BooleanToBrushConverter : IValueConverter
{
    public Brush TrueBrush { get; set; } = new SolidColorBrush(Color.FromRgb(34, 197, 94));
    public Brush FalseBrush { get; set; } = new SolidColorBrush(Color.FromRgb(107, 114, 128));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? TrueBrush : FalseBrush;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
