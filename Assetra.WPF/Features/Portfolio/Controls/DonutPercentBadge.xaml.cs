using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Assetra.WPF.Features.Portfolio.Controls;

/// <summary>
/// P5.10 — Compact donut percentage badge used in allocation lists.
/// Three DPs drive visual:
/// <list type="bullet">
///   <item><c>Percent</c> 0–100 — drives arc length via PercentToDashArrayConverter.</item>
///   <item><c>FillBrush</c> — arc color (typically asset color).</item>
///   <item><c>PercentText</c> — text in center (caller formats, e.g., "63.0%" / "8.4%" / "<0.1%").</item>
/// </list>
/// </summary>
public partial class DonutPercentBadge : UserControl
{
    public DonutPercentBadge() => InitializeComponent();

    public static readonly DependencyProperty PercentProperty =
        DependencyProperty.Register(
            nameof(Percent),
            typeof(double),
            typeof(DonutPercentBadge),
            new PropertyMetadata(0.0));

    public double Percent
    {
        get => (double)GetValue(PercentProperty);
        set => SetValue(PercentProperty, value);
    }

    public static readonly DependencyProperty FillBrushProperty =
        DependencyProperty.Register(
            nameof(FillBrush),
            typeof(Brush),
            typeof(DonutPercentBadge),
            new PropertyMetadata(null));

    public Brush? FillBrush
    {
        get => (Brush?)GetValue(FillBrushProperty);
        set => SetValue(FillBrushProperty, value);
    }

    public static readonly DependencyProperty PercentTextProperty =
        DependencyProperty.Register(
            nameof(PercentText),
            typeof(string),
            typeof(DonutPercentBadge),
            new PropertyMetadata(string.Empty));

    public string PercentText
    {
        get => (string)GetValue(PercentTextProperty);
        set => SetValue(PercentTextProperty, value);
    }
}
