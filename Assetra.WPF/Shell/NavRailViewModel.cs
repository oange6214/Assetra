using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Assetra.WPF.Shell;

public partial class NavRailViewModel : ObservableObject
{
    // ── Navigation history ─────────────────────────────────────────────────

    private readonly Stack<NavSection> _backStack = new();
    private readonly Stack<NavSection> _forwardStack = new();
    private bool _isHistoryNavigation;

    // Manual property so the setter routes through NavigateTo(),
    // keeping history stacks in sync regardless of how callers set the section.
    private NavSection _activeSection = NavSection.Portfolio;
    public NavSection ActiveSection
    {
        get => _activeSection;
        set => NavigateTo(value);
    }

    public bool CanGoBack    => _backStack.Count > 0;
    public bool CanGoForward => _forwardStack.Count > 0;

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void GoBack()
    {
        if (!_backStack.TryPop(out var prev)) return;
        _forwardStack.Push(_activeSection);
        _isHistoryNavigation = true;
        NavigateTo(prev);
        _isHistoryNavigation = false;
    }

    [RelayCommand(CanExecute = nameof(CanGoForward))]
    private void GoForward()
    {
        if (!_forwardStack.TryPop(out var next)) return;
        _backStack.Push(_activeSection);
        _isHistoryNavigation = true;
        NavigateTo(next);
        _isHistoryNavigation = false;
    }

    /// <summary>
    /// All navigation goes through here — called by the ActiveSection setter,
    /// GoBack, GoForward, and any external callers.
    /// </summary>
    public void NavigateTo(NavSection section)
    {
        if (section == _activeSection) return;

        if (!_isHistoryNavigation)
        {
            _backStack.Push(_activeSection);
            _forwardStack.Clear();
        }

        SetProperty(ref _activeSection, section, nameof(ActiveSection));
        NotifyActiveSectionDependents();
        GoBackCommand.NotifyCanExecuteChanged();
        GoForwardCommand.NotifyCanExecuteChanged();
    }

    private void NotifyActiveSectionDependents()
    {
        OnPropertyChanged(nameof(IsPortfolioActive));
        OnPropertyChanged(nameof(IsFinancialOverviewActive));
        OnPropertyChanged(nameof(IsAlertsActive));
        OnPropertyChanged(nameof(IsSettingsActive));
    }

    // ── Section predicates (used by MainWindow.xaml visibility bindings) ───

    public bool IsPortfolioActive         => ActiveSection == NavSection.Portfolio;
    public bool IsFinancialOverviewActive => ActiveSection == NavSection.FinancialOverview;
    public bool IsAlertsActive            => ActiveSection == NavSection.Alerts;
    public bool IsSettingsActive          => ActiveSection == NavSection.Settings;
}
