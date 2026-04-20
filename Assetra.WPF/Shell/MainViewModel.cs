using System.Reactive.Disposables;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Assetra.WPF.Features.Alerts;
using Assetra.WPF.Features.Allocation;
using Assetra.WPF.Features.Portfolio;
using Assetra.WPF.Features.Settings;
using Assetra.WPF.Features.Snackbar;
using Assetra.WPF.Features.StatusBar;
using Assetra.WPF.Infrastructure;
using Wpf.Ui.Appearance;

namespace Assetra.WPF.Shell;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly CompositeDisposable _disposables = new();

    public NavRailViewModel NavRail { get; }
    public StatusBarViewModel StatusBar { get; }
    public PortfolioViewModel Portfolio { get; }
    public AllocationViewModel Allocation { get; }
    public FinancialOverviewViewModel FinancialOverview { get; }
    public AlertsViewModel Alerts { get; }
    public SettingsViewModel Settings { get; }
    public SnackbarViewModel Snackbar { get; }

    // Title bar search / command palette
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private bool _isSearchOpen;

    [RelayCommand]
    private void ToggleSearch() => IsSearchOpen = !IsSearchOpen;

    // Clear the query whenever the popup closes — covers outside-click dismiss
    // (two-way bound from Popup.IsOpen) as well as the toggle command path.
    partial void OnIsSearchOpenChanged(bool value)
    {
        if (!value)
            SearchText = string.Empty;
    }

    // Navigation

    [RelayCommand]
    private void GoToSettings() => NavRail.ActiveSection = NavSection.Settings;

    // Theme

    private readonly IThemeService _themeService;

    [ObservableProperty] private ApplicationTheme _currentTheme;

    public string ThemeToggleLabel => CurrentTheme == ApplicationTheme.Dark ? "淺色" : "深色";
    public string ThemeToggleIcon => CurrentTheme == ApplicationTheme.Dark ? "☀️" : "🌙";

    partial void OnCurrentThemeChanged(ApplicationTheme value)
    {
        OnPropertyChanged(nameof(ThemeToggleLabel));
        OnPropertyChanged(nameof(ThemeToggleIcon));
    }

    [RelayCommand]
    private void ToggleTheme()
    {
        var next = CurrentTheme == ApplicationTheme.Dark
            ? ApplicationTheme.Light
            : ApplicationTheme.Dark;
        _themeService.Apply(next);
        CurrentTheme = next;
    }

    public MainViewModel(
        NavRailViewModel navRail,
        StatusBarViewModel statusBar,
        PortfolioViewModel portfolio,
        AllocationViewModel allocation,
        FinancialOverviewViewModel financialOverview,
        AlertsViewModel alerts,
        SettingsViewModel settings,
        SnackbarViewModel snackbar,
        IThemeService themeService)
    {
        NavRail = navRail;
        StatusBar = statusBar;
        Portfolio = portfolio;
        Allocation = allocation;
        FinancialOverview = financialOverview;
        Alerts = alerts;
        Settings = settings;
        Snackbar = snackbar;
        _themeService = themeService;
        CurrentTheme = themeService.CurrentTheme;
    }

    public void Dispose()
    {
        WeakReferenceMessenger.Default.UnregisterAll(this);
        _disposables.Dispose();
    }
}
