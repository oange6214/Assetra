using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using Assetra.Infrastructure.Persistence;

namespace Assetra.WPF.Shell;

public partial class SplashScreen : Window
{
    private const int TotalSteps = 6;
    private int _currentStep;

    public SplashScreen()
    {
        InitializeComponent();
        var uiScale = AppSettingsService.LoadSettings().UiScale;
        RootBorder.LayoutTransform = new ScaleTransform(uiScale, uiScale);
        ApplyThemedIcon();
    }

    /// <summary>
    /// Advance the progress bar by one step and display the localized status message.
    /// </summary>
    /// <param name="resourceKey">Language resource key for the status text (e.g. "Splash.Watchlist").</param>
    public void Advance(string resourceKey)
    {
        _currentStep++;
        var percent = (double)_currentStep / TotalSteps * 100;
        LoadingBar.Value = percent;
        StatusText.Text = TryFindResource(resourceKey) as string ?? resourceKey;
    }

    private void ApplyThemedIcon()
    {
        var backgroundBrush = TryFindResource("AppBackground") as SolidColorBrush;
        var color = backgroundBrush?.Color ?? Colors.Black;
        var luminance = ((0.2126 * color.R) + (0.7152 * color.G) + (0.0722 * color.B)) / 255d;
        var isLightTheme = luminance > 0.6d;
        var iconPath = isLightTheme
            ? "pack://application:,,,/Assetra.WPF;component/Assets/png/asset-logo-light-128.png"
            : "pack://application:,,,/Assetra.WPF;component/Assets/png/asset-logo-dark-128.png";

        SplashIcon.Source = new BitmapImage(new Uri(iconPath, UriKind.Absolute));
    }
}
