using Assetra.Core.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Assetra.WPF.Shell;

public partial class NavRailViewModel : ObservableObject
{
    private readonly Stack<NavSection> _backStack = new();
    private readonly Stack<NavSection> _forwardStack = new();
    private bool _isHistoryNavigation;

    public NavRailViewModel() { }

    /// <summary>
    /// DI-friendly constructor. Initial section is read from
    /// <see cref="AppSettings.DefaultHomeSection"/> so each user can pin
    /// their preferred startup landing page (default = FinancialOverview).
    /// </summary>
    public NavRailViewModel(IAppSettingsService settings)
    {
        _activeSection = ResolveInitialSection(settings.Current.DefaultHomeSection);
    }

    private static NavSection ResolveInitialSection(string raw)
    {
        if (!string.IsNullOrWhiteSpace(raw)
            && Enum.TryParse<NavSection>(raw, ignoreCase: true, out var parsed))
            return parsed;
        return NavSection.FinancialOverview;
    }

    // Manual property so the setter routes through NavigateTo(),
    // keeping history stacks in sync regardless of how callers set the section.
    private NavSection _activeSection = NavSection.FinancialOverview;
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
        try
        {
            NavigateTo(prev);
        }
        finally
        {
            _isHistoryNavigation = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanGoForward))]
    private void GoForward()
    {
        if (!_forwardStack.TryPop(out var next)) return;
        _backStack.Push(_activeSection);
        _isHistoryNavigation = true;
        try
        {
            NavigateTo(next);
        }
        finally
        {
            _isHistoryNavigation = false;
        }
    }

    /// <summary>
    /// String overload for XAML KeyBinding.CommandParameter use. Parses the
    /// argument as <see cref="NavSection"/> and delegates. Unknown strings
    /// silently no-op so a typo'd shortcut doesn't crash.
    /// </summary>
    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void NavigateToByName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        if (Enum.TryParse<NavSection>(name, ignoreCase: true, out var section))
            NavigateTo(section);
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
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
        GoBackCommand.NotifyCanExecuteChanged();
        GoForwardCommand.NotifyCanExecuteChanged();
    }
}
