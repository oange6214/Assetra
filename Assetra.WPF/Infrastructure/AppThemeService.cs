using System.IO;
using System.Windows;

namespace Assetra.WPF.Infrastructure;

public sealed class AppThemeService : IThemeService
{
    private static readonly string ThemeFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Assetra", "theme.txt");

    public ApplicationTheme CurrentTheme { get; private set; }

    /// <inheritdoc/>
    public event Action<ApplicationTheme>? ThemeChanged;

    public AppThemeService()
    {
        CurrentTheme = LoadSavedTheme();
        ApplyInternal(CurrentTheme);
    }

    public void Apply(ApplicationTheme theme)
    {
        CurrentTheme = theme;
        ApplyInternal(theme);
        SaveTheme(theme);
        ThemeChanged?.Invoke(theme);
    }

    private static void ApplyInternal(ApplicationTheme theme)
    {
        // 1. Swap Assetra's semantic palette (Dark.xaml <-> Light.xaml).
        SwapCustomDictionary(theme);

        // 2. Re-apply colour-scheme convention (Taiwan / International).
        ColorSchemeService.ReapplyCurrentScheme(theme);
    }

    // Custom palette swap

    private static void SwapCustomDictionary(ApplicationTheme theme)
    {
        var dicts = System.Windows.Application.Current.Resources.MergedDictionaries;
        var paletteName = theme == ApplicationTheme.Light ? "Light" : "Dark";
        var newDict = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/Assetra.WPF;component/DesignSystem/Themes/{paletteName}.xaml")
        };

        // In-place replacement preserves index order and avoids a momentary gap
        // where DynamicResource tokens (AppBackground, etc.) are undefined.
        for (var i = 0; i < dicts.Count; i++)
        {
            var src = dicts[i].Source?.ToString();
            if (src is null)
                continue;
            if (src.Contains("DesignSystem/Themes/Dark.xaml", StringComparison.OrdinalIgnoreCase) ||
                src.Contains("DesignSystem/Themes/Light.xaml", StringComparison.OrdinalIgnoreCase))
            {
                dicts[i] = newDict;
                return;
            }
        }

        // Palette not yet in the collection (first run before any swap) — append.
        dicts.Add(newDict);
    }

    // Persistence

    private static ApplicationTheme LoadSavedTheme()
    {
        try
        {
            if (File.Exists(ThemeFile))
            {
                var text = File.ReadAllText(ThemeFile).Trim();
                if (Enum.TryParse<ApplicationTheme>(text, out var saved))
                    return saved;
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Failed to read saved theme from disk; using default (Dark)");
        }
        return ApplicationTheme.Dark;
    }

    private static void SaveTheme(ApplicationTheme theme)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ThemeFile)!);
            File.WriteAllText(ThemeFile, theme.ToString());
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Failed to persist theme preference to disk");
        }
    }
}
