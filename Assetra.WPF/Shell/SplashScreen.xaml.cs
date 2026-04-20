using System.Windows;

namespace Assetra.WPF.Shell;

public partial class SplashScreen : Window
{
    private const int TotalSteps = 6;
    private int _currentStep;

    public SplashScreen()
    {
        InitializeComponent();
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
}
