using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;

namespace Assetra.WPF.Infrastructure.Converters;

/// <summary>
/// 把 double[] 轉成 LiveCharts <see cref="ISeries"/>[] 給 sparkline 用的 mini line chart。
/// 線色：上漲 = Brush.Up（台灣紅）、下跌 = Brush.Down（台灣綠）— 從當下 theme resource 解析，
/// 不再硬碼 hex，跟 design system 的「不用 hex」規則對齊。
///
/// Why a converter？sparkline 每列都要產生一組 series，在 row VM 直接做會把 LiveCharts
/// 依賴洩漏到 row 層；用 converter 維持 row VM 只持有原始 double[]。
/// </summary>
public sealed class SparklineSeriesConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not double[] pts || pts.Length < 2)
            return Array.Empty<ISeries>();

        var isUp = pts[^1] >= pts[0];
        var skColor = ResolveThemeSkColor(isUp ? "Brush.Up" : "Brush.Down",
                                          isUp ? 0xFFEF4444u : 0xFF22C55Eu);
        return new ISeries[]
        {
            new LineSeries<double>
            {
                Values = pts,
                Fill = null,
                Stroke = new SolidColorPaint(skColor) { StrokeThickness = 1.5f, IsStroke = true },
                GeometrySize = 0,
                LineSmoothness = 0.4,
                AnimationsSpeed = TimeSpan.Zero,
            }
        };
    }

    /// <summary>
    /// 從 Application 資源解析 Brush.* token 成 SkiaSharp 用的 SKColor。Fallback 走
    /// hardcoded 預設色避免 design-time / pre-theme-init 情境拋。
    /// </summary>
    private static SKColor ResolveThemeSkColor(string brushKey, uint fallbackArgb)
    {
        try
        {
            if (System.Windows.Application.Current?.TryFindResource(brushKey) is SolidColorBrush brush)
            {
                var c = brush.Color;
                return new SKColor(c.R, c.G, c.B, c.A);
            }
        }
        catch
        {
            // Application.Current may be null at design time — fall through to fallback.
        }
        return new SKColor(fallbackArgb);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
