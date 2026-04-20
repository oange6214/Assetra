namespace Assetra.Core.Interfaces;

/// <summary>
/// Abstracts UI-string localization and dynamic language switching.
/// The active language is stored in <see cref="AppSettings.Language"/> and
/// applied at startup; changing it at runtime swaps the WPF ResourceDictionary
/// so all <c>{DynamicResource}</c> bindings update without a restart.
/// </summary>
public interface ILocalizationService
{
    /// <summary>BCP-47 tag of the currently active language (e.g. "zh-TW", "en-US").</summary>
    string CurrentLanguage { get; }

    /// <summary>Looks up a localised string by key. Returns <paramref name="fallback"/> when not found.</summary>
    string Get(string key, string fallback = "");

    /// <summary>
    /// Switches the active language, swaps the resource dictionary, and fires
    /// <see cref="LanguageChanged"/> so ViewModels can refresh dynamic strings.
    /// </summary>
    void SetLanguage(string languageCode);

    /// <summary>Fired on the UI thread after the active language has changed.</summary>
    event EventHandler? LanguageChanged;
}
