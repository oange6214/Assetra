using System.Windows;
using System.Windows.Media;
using Wpf.Ui.Appearance;

namespace Assetra.WPF.Infrastructure;

/// <summary>
/// Manages the 漲跌配色慣例 (price-direction color convention) at runtime.
///
/// Taiwan convention  (台灣慣例): 漲紅跌綠 — AppUp = red,   AppDown = green.
/// International      (國際慣例): 漲綠跌紅 — AppUp = green, AppDown = red  (theme default).
///
/// Works by overriding AppUp / AppDown directly in Application.Current.Resources,
/// which takes precedence over any merged theme dictionary and is automatically
/// picked up by all DynamicResource bindings.
/// </summary>
public static class ColorSchemeService
{
    // Dark palette (matches Dark.xaml)
    private static readonly Color DarkGreen = Color.FromRgb(12, 187, 138);  // #0CBB8A
    private static readonly Color DarkRed = Color.FromRgb(242, 75, 75);  // #F24B4B

    // Light palette (matches Light.xaml)
    private static readonly Color LightGreen = Color.FromRgb(13, 122, 91);   // #0D7A5B
    private static readonly Color LightRed = Color.FromRgb(196, 43, 28);   // #C42B1C

    private static bool _taiwanConvention;

    public static bool TaiwanConvention => _taiwanConvention;

    /// <summary>Raised whenever the convention or theme changes, so consumers
    /// (e.g. non-DynamicResource brushes) can re-evaluate their colours.</summary>
    public static event Action? SchemeChanged;

    /// <summary>Apply a new convention and immediately update app resources.</summary>
    public static void Apply(bool taiwan, ApplicationTheme theme)
    {
        _taiwanConvention = taiwan;
        ReapplyCurrentScheme(theme);
    }

    /// <summary>
    /// Re-apply the stored convention for the given theme.
    /// Called automatically by <see cref="AppThemeService"/> after every theme switch
    /// so that Taiwan shades stay consistent with the active palette.
    /// </summary>
    public static void ReapplyCurrentScheme(ApplicationTheme theme)
    {
        var resources = Application.Current?.Resources;
        if (resources is null)
            return;

        if (!_taiwanConvention)
        {
            // International: remove overrides and let the theme dictionaries control colours.
            resources.Remove("AppUp");
            resources.Remove("AppDown");
            SchemeChanged?.Invoke();
            return;
        }

        // Taiwan: 漲紅跌綠 — swap the colours using theme-appropriate shades.
        bool isDark = theme != ApplicationTheme.Light;
        resources["AppUp"] = new SolidColorBrush(isDark ? DarkRed : LightRed);
        resources["AppDown"] = new SolidColorBrush(isDark ? DarkGreen : LightGreen);
        SchemeChanged?.Invoke();
    }
}
