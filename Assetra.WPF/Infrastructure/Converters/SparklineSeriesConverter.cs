using System.Globalization;
using System.Windows.Data;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;

namespace Assetra.WPF.Infrastructure.Converters;

/// <summary>
/// 把 double[] 轉成 LiveCharts <see cref="ISeries"/>[] 給 sparkline 用的 mini line chart。
/// 線色：上漲紅、下跌綠（台灣慣例）。失敗 / 空資料 → 空 series（圖表內容空白）。
///
/// Why a converter？sparkline 每列都要產生一組 series，在 row VM 直接做會把 LiveCharts
/// 依賴洩漏到 row 層；用 converter 維持 row VM 只持有原始 double[]。
/// </summary>
public sealed class SparklineSeriesConverter : IValueConverter
{
    private static readonly SolidColorPaint UpPaint = new(SKColor.Parse("#EF4444"))
    {
        StrokeThickness = 1.5f,
        IsStroke = true,
    };
    private static readonly SolidColorPaint DownPaint = new(SKColor.Parse("#22C55E"))
    {
        StrokeThickness = 1.5f,
        IsStroke = true,
    };

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not double[] pts || pts.Length < 2)
            return Array.Empty<ISeries>();

        var paint = pts[^1] >= pts[0] ? UpPaint : DownPaint;
        return new ISeries[]
        {
            new LineSeries<double>
            {
                Values = pts,
                Fill = null,
                Stroke = paint,
                GeometrySize = 0,
                LineSmoothness = 0.4,
                AnimationsSpeed = TimeSpan.Zero,
            }
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
