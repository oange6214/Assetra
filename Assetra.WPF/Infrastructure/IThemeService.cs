using Wpf.Ui.Appearance;

namespace Assetra.WPF.Infrastructure;

public interface IThemeService
{
    ApplicationTheme CurrentTheme { get; }

    /// <summary>Raised on the UI thread after every theme switch completes.</summary>
    event Action<ApplicationTheme>? ThemeChanged;

    void Apply(ApplicationTheme theme);
}
