using System.IO;
using System.Windows;
using Wpf.Ui.Appearance;

namespace Assetra.WPF.Infrastructure;

public sealed class AppThemeService : IThemeService
{
    private static readonly string ThemeFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Assetra", "theme.txt");

    /// <summary>WPF-UI theme dictionary URI prefix.</summary>
    private const string WpfUiThemePath =
        "pack://application:,,,/Wpf.Ui;component/Resources/Theme/";

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
        // 1. Swap WPF-UI's own ThemesDictionary (Light / Dark control styles).
        // Keep this resource-only for now: ApplicationThemeManager.Apply()
        // also takes over the FluentWindow chrome/backdrop and requires
        // ExtendsContentIntoTitleBar=True. Assetra still has a custom shell
        // (TitleBar + NavRail + content grid), so that runtime chrome handoff
        // can collapse the shell into a blank/unstyled surface.
        SwapWpfUiThemeDictionary(theme);

        // 2. Swap Assetra's semantic palette (Dark.xaml <-> Light.xaml). These
        // tokens intentionally stay as aliases above WPF-UI's control resources,
        // and also override AccentFillColor* / SystemAccentColor* keys so Wpf.Ui
        // controls (Primary button, NavView indicator, ToggleSwitch focus, ...)
        // pin to AppAccent instead of the Windows colorization color.
        //
        // ApplicationAccentColorManager.Apply() is intentionally NOT called:
        // it sets accent brushes directly on Application.Resources (not in a
        // MergedDictionary), which would beat our overrides and re-introduce
        // the Light/Dark shade drift on every toggle.
        SwapCustomDictionary(theme);

        // 3. Re-apply colour-scheme convention (Taiwan / International).
        ColorSchemeService.ReapplyCurrentScheme(theme);
    }

    // WPF-UI ThemesDictionary resource-only swap

    /// <summary>
    /// In-place replacement of the WPF-UI theme resource dictionary.
    /// </summary>
    private static void SwapWpfUiThemeDictionary(ApplicationTheme theme)
    {
        var themeName = theme == ApplicationTheme.Light ? "Light" : "Dark";
        var newUri = new Uri(WpfUiThemePath + themeName + ".xaml", UriKind.Absolute);
        var dicts = System.Windows.Application.Current.Resources.MergedDictionaries;

        for (var i = 0; i < dicts.Count; i++)
        {
            var src = dicts[i].Source?.ToString();
            if (src is not null
                && src.Contains("wpf.ui;", StringComparison.OrdinalIgnoreCase)
                && src.Contains("theme", StringComparison.OrdinalIgnoreCase))
            {
                dicts[i] = new ResourceDictionary { Source = newUri };
                return;
            }

            // Also check nested merged dictionaries (ThemesDictionary nests its content)
            for (var j = 0; j < dicts[i].MergedDictionaries.Count; j++)
            {
                var innerSrc = dicts[i].MergedDictionaries[j]?.Source?.ToString();
                if (innerSrc is not null
                    && innerSrc.Contains("wpf.ui;", StringComparison.OrdinalIgnoreCase)
                    && innerSrc.Contains("theme", StringComparison.OrdinalIgnoreCase))
                {
                    dicts[i].MergedDictionaries[j] = new ResourceDictionary { Source = newUri };
                    return;
                }
            }
        }
    }

    // Custom palette swap

    private static void SwapCustomDictionary(ApplicationTheme theme)
    {
        var dicts = System.Windows.Application.Current.Resources.MergedDictionaries;
        var paletteName = theme == ApplicationTheme.Light ? "Light" : "Dark";
        var newDict = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/Assetra.WPF;component/Themes/{paletteName}.xaml")
        };

        // In-place replacement preserves index order and avoids a momentary gap
        // where DynamicResource tokens (AppBackground, etc.) are undefined.
        for (var i = 0; i < dicts.Count; i++)
        {
            var src = dicts[i].Source?.ToString();
            if (src is null)
                continue;
            if (src.Contains("Assetra.WPF;component/Themes/Dark.xaml", StringComparison.OrdinalIgnoreCase) ||
                src.Contains("Assetra.WPF;component/Themes/Light.xaml", StringComparison.OrdinalIgnoreCase))
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
