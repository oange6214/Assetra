using System.Windows.Media;

namespace Assetra.WPF.Infrastructure;

/// <summary>
/// Shared 7-step color palette for gain/loss visualization (treemaps, heatmaps).
/// Respects <see cref="ColorSchemeService.TaiwanConvention"/>:
///   Taiwan        (漲紅跌綠): gains = red shades,   losses = green shades
///   International (漲綠跌紅): gains = green shades, losses = red shades
/// Buckets: ±3%, ±2%, ±0.2%, and flat; neutral is always gray.
/// </summary>
public static class PnlColorPalette
{
    // Shared shades — ColorSchemeService decides which side maps to gains vs losses.
    public static readonly SolidColorBrush StrongRed = Frozen("#C0392B");
    public static readonly SolidColorBrush MediumRed = Frozen("#E57373");
    public static readonly SolidColorBrush LightRed = Frozen("#F4A5A5");
    public static readonly SolidColorBrush Neutral = Frozen("#4A4A4A");
    public static readonly SolidColorBrush LightGreen = Frozen("#6B8F5E");
    public static readonly SolidColorBrush MediumGreen = Frozen("#4A7F3E");
    public static readonly SolidColorBrush StrongGreen = Frozen("#2E5A22");

    /// <summary>
    /// Pick a tile color based on a signed percent change.
    /// Passing <paramref name="isNeutralRow"/> = true always returns <see cref="Neutral"/>
    /// (e.g. cash rows that have no meaningful direction).
    /// </summary>
    public static SolidColorBrush Pick(double percent, bool isNeutralRow = false)
    {
        if (isNeutralRow)
            return Neutral;

        bool taiwan = ColorSchemeService.TaiwanConvention;
        return percent switch
        {
            >= 3.0 => taiwan ? StrongRed : StrongGreen,
            >= 2.0 => taiwan ? MediumRed : MediumGreen,
            >= 0.2 => taiwan ? LightRed : LightGreen,
            <= -3.0 => taiwan ? StrongGreen : StrongRed,
            <= -2.0 => taiwan ? MediumGreen : MediumRed,
            <= -0.2 => taiwan ? LightGreen : LightRed,
            _ => Neutral,
        };
    }

    /// <summary>
    /// Smooth continuous gradient for treemap tiles, staying within the gain/loss hue family.
    /// <para>
    /// Interpolates from a <b>dark</b> version of the gain/loss color (small magnitude) to a
    /// <b>vivid</b> version (large magnitude).  This avoids the muddy brown midpoints produced
    /// by interpolating from neutral gray through RGB space.
    /// </para>
    /// <para>
    /// Taiwan (漲紅跌綠):
    ///   gain  → dark maroon #7A2020 … vivid red   #F44336  <br/>
    ///   loss  → dark forest #1A4D2E … vivid green #4CAF50
    /// </para>
    /// <para>
    /// International (漲綠跌紅): colors swapped.
    /// </para>
    /// Returns a new frozen <see cref="SolidColorBrush"/> per call (one per tile per rebuild).
    /// </summary>
    public static SolidColorBrush PickGradient(double percent, bool isNeutralRow = false)
    {
        if (isNeutralRow)
            return Neutral;

        // Near-zero: show neutral rather than an almost-invisible tint.
        if (Math.Abs(percent) < 0.25)
            return Neutral;

        bool taiwan       = ColorSchemeService.TaiwanConvention;
        bool isPositivePnl = percent >= 0;

        // ── Color anchors (dark → vivid within the same hue family) ─────────
        // Taiwan convention: gain = red, loss = green.
        // International:     gain = green, loss = red.
        Color gainDark, gainVivid, lossDark, lossVivid;
        if (taiwan)
        {
            gainDark  = Color.FromRgb(0x7A, 0x20, 0x20);  // deep maroon
            gainVivid = Color.FromRgb(0xF4, 0x43, 0x36);  // Material Red 500
            lossDark  = Color.FromRgb(0x1A, 0x4D, 0x2E);  // deep forest green
            lossVivid = Color.FromRgb(0x4C, 0xAF, 0x50);  // Material Green 500
        }
        else
        {
            gainDark  = Color.FromRgb(0x1A, 0x4D, 0x2E);  // deep forest green
            gainVivid = Color.FromRgb(0x4C, 0xAF, 0x50);  // Material Green 500
            lossDark  = Color.FromRgb(0x7A, 0x20, 0x20);  // deep maroon
            lossVivid = Color.FromRgb(0xF4, 0x43, 0x36);  // Material Red 500
        }

        Color dark  = isPositivePnl ? gainDark  : lossDark;
        Color vivid = isPositivePnl ? gainVivid : lossVivid;

        // ── Normalise magnitude ──────────────────────────────────────────────
        // t = 0 → small (dark anchor)   t = 1 → ≥20 % (vivid anchor)
        double raw = Math.Clamp(Math.Abs(percent) / 20.0, 0.0, 1.0);
        // Gamma < 1 spreads low values: +1 % and +3 % look noticeably different.
        double t = Math.Pow(raw, 0.65);

        var color = LerpColor(dark, vivid, t);
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Color LerpColor(Color a, Color b, double t)
    {
        return Color.FromRgb(
            (byte)Math.Round(a.R + (b.R - a.R) * t),
            (byte)Math.Round(a.G + (b.G - a.G) * t),
            (byte)Math.Round(a.B + (b.B - a.B) * t));
    }

    private static SolidColorBrush Frozen(string hex)
    {
        var c = (Color)ColorConverter.ConvertFromString(hex)!;
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
