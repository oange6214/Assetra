using Assetra.Core.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Assetra.WPF.Shell;

/// <summary>
/// First-run welcome overlay gate. Shows a one-time welcome card that guides the
/// user to add their first holding (the golden onboarding path). Reuses the
/// existing <see cref="Core.Models.AppSettings.HasShownWelcomeBanner"/> flag as the
/// "already seen" gate: shown only while the flag is false.
///
/// Kept as a tiny standalone VM (owns nothing but settings + a nav callback) so the
/// gating logic unit-tests without the full shell. <see cref="MainViewModel"/> owns
/// one instance and points the overlay's DataContext at it.
/// </summary>
public partial class WelcomeGateViewModel : ObservableObject
{
    private readonly IAppSettingsService _settings;
    private readonly Action<NavSection> _navigate;

    /// <summary>Overlay visibility gate. Initialised from the persisted seen-flag.</summary>
    [ObservableProperty] private bool _showWelcome;

    public WelcomeGateViewModel(IAppSettingsService settings, Action<NavSection> navigate)
    {
        _settings = settings;
        _navigate = navigate;
        ShowWelcome = !_settings.Current.HasShownWelcomeBanner;
    }

    /// <summary>
    /// Marks the welcome as seen and hides the overlay. raiseChanged: false —
    /// the seen-flag is pure bookkeeping and must NOT drive the app-wide
    /// IAppSettingsService.Changed reload (見 settings-changed 回饋迴圈).
    /// </summary>
    [RelayCommand]
    private async Task DismissWelcomeAsync()
    {
        await _settings.SaveAsync(
            _settings.Current with { HasShownWelcomeBanner = true },
            raiseChanged: false);
        ShowWelcome = false;
    }

    /// <summary>
    /// Golden path CTA: dismiss the overlay, then navigate to the Portfolio
    /// section where the prominent「＋ 新增交易」entry point lives.
    /// </summary>
    [RelayCommand]
    private async Task StartAddFirstHoldingAsync()
    {
        await DismissWelcomeAsync();
        _navigate(NavSection.Portfolio);
    }
}
