using System.Globalization;
using System.Threading;
using System.Windows;
using System.Windows.Markup;
using Assetra.Core.Interfaces;
using Serilog;

namespace Assetra.WPF.Infrastructure;

/// <summary>
/// Null-object implementation of <see cref="ILocalizationService"/> that always returns
/// the supplied fallback value. Used in tests and ViewModels constructed without DI.
/// </summary>
internal sealed class NullLocalizationService : ILocalizationService
{
    public static readonly NullLocalizationService Instance = new();
    public string CurrentLanguage => "zh-TW";
    public event EventHandler? LanguageChanged { add { } remove { } }
    public string Get(string key, string fallback = "") => fallback;
    public void SetLanguage(string languageCode) { }
}

/// <summary>
/// Swaps a WPF <see cref="ResourceDictionary"/> named after the language code
/// (e.g. <c>Languages/zh-TW.xaml</c>) so every <c>{DynamicResource}</c> binding
/// in XAML updates automatically without restarting the application.
/// </summary>
public sealed class WpfLocalizationService : ILocalizationService
{
    private const string DictUriFormat =
        "pack://application:,,,/Assetra.WPF;component/Languages/{0}.xaml";

    public string CurrentLanguage { get; private set; } = "zh-TW";

    public event EventHandler? LanguageChanged;

    public WpfLocalizationService()
    {
        ApplyCulture(CurrentLanguage);
    }

    public string Get(string key, string fallback = "")
        => System.Windows.Application.Current?.Resources[key] as string ?? fallback;

    public void SetLanguage(string languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
            return;
        try
        {
            ApplyCulture(languageCode);

            if (CurrentLanguage == languageCode)
            {
                LanguageChanged?.Invoke(this, EventArgs.Empty);
                return;
            }

            var uri = new Uri(string.Format(DictUriFormat, languageCode));
            var dict = new ResourceDictionary { Source = uri };

            var merged = System.Windows.Application.Current.Resources.MergedDictionaries;
            var old = merged.FirstOrDefault(d =>
                d.Source?.OriginalString.Contains("/Languages/") == true);
            if (old is not null)
                merged.Remove(old);
            merged.Add(dict);

            CurrentLanguage = languageCode;
            LanguageChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to switch language to {LanguageCode}", languageCode);
        }
    }

    private static void ApplyCulture(string languageCode)
    {
        var culture = CultureInfo.GetCultureInfo(languageCode);
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;

        if (System.Windows.Application.Current is { } app)
        {
            var language = XmlLanguage.GetLanguage(culture.IetfLanguageTag);
            app.MainWindow?.SetValue(FrameworkElement.LanguageProperty, language);
            foreach (Window window in app.Windows)
                window.SetValue(FrameworkElement.LanguageProperty, language);
        }
    }
}
