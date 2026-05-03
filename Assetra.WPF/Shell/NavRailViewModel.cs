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

    public NavSection SelectedRailSection
    {
        get => GetRailSection(_activeSection);
        set => NavigateTo(value);
    }

    public NavSection SelectedCashflowTab
    {
        get => _activeSection switch
        {
            NavSection.Recurring => NavSection.Recurring,
            _ => NavSection.Categories,
        };
        set => NavigateTo(value);
    }

    public NavSection SelectedInsightsTab
    {
        get => _activeSection switch
        {
            NavSection.Trends => NavSection.Trends,
            NavSection.Reports => NavSection.Reports,
            NavSection.Fire => NavSection.Fire,
            NavSection.MonteCarlo => NavSection.MonteCarlo,
            _ => NavSection.Goals,
        };
        set => NavigateTo(value);
    }

    public NavSection SelectedMultiAssetTab
    {
        get => _activeSection switch
        {
            NavSection.Insurance => NavSection.Insurance,
            NavSection.Retirement => NavSection.Retirement,
            NavSection.PhysicalAsset => NavSection.PhysicalAsset,
            _ => NavSection.RealEstate,
        };
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
        NotifyHistoryStateChanged();
    }

    private void NotifyHistoryStateChanged()
    {
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
        GoBackCommand.NotifyCanExecuteChanged();
        GoForwardCommand.NotifyCanExecuteChanged();
    }

    private void NotifyActiveSectionDependents()
    {
        OnPropertyChanged(nameof(SelectedRailSection));
        OnPropertyChanged(nameof(SelectedCashflowTab));
        OnPropertyChanged(nameof(SelectedInsightsTab));
        OnPropertyChanged(nameof(SelectedMultiAssetTab));
        OnPropertyChanged(nameof(IsPortfolioActive));
        OnPropertyChanged(nameof(IsFinancialOverviewActive));
        OnPropertyChanged(nameof(IsCashflowActive));
        OnPropertyChanged(nameof(IsInsightsActive));
        OnPropertyChanged(nameof(IsMultiAssetActive));
        OnPropertyChanged(nameof(IsCategoriesActive));
        OnPropertyChanged(nameof(IsRecurringActive));
        OnPropertyChanged(nameof(IsReportsActive));
        OnPropertyChanged(nameof(IsTrendsActive));
        OnPropertyChanged(nameof(IsGoalsActive));
        OnPropertyChanged(nameof(IsAlertsActive));
        OnPropertyChanged(nameof(IsImportActive));
        OnPropertyChanged(nameof(IsRealEstateActive));
        OnPropertyChanged(nameof(IsInsuranceActive));
        OnPropertyChanged(nameof(IsRetirementActive));
        OnPropertyChanged(nameof(IsPhysicalAssetActive));
        OnPropertyChanged(nameof(IsFireActive));
        OnPropertyChanged(nameof(IsMonteCarloActive));
        OnPropertyChanged(nameof(IsSettingsActive));
    }

    private static NavSection GetRailSection(NavSection section) => section switch
    {
        NavSection.Categories or NavSection.Recurring => NavSection.Cashflow,
        NavSection.Reports or NavSection.Trends or NavSection.Goals or NavSection.Fire or NavSection.MonteCarlo
            => NavSection.Insights,
        NavSection.RealEstate or NavSection.Insurance or NavSection.Retirement or NavSection.PhysicalAsset
            => NavSection.MultiAsset,
        _ => section,
    };

    // ── Section predicates (used by MainWindow.xaml visibility bindings) ───

    public bool IsPortfolioActive         => ActiveSection == NavSection.Portfolio;
    public bool IsFinancialOverviewActive => ActiveSection == NavSection.FinancialOverview;
    public bool IsCashflowActive          => ActiveSection == NavSection.Cashflow;
    public bool IsInsightsActive          => ActiveSection == NavSection.Insights;
    public bool IsMultiAssetActive        => ActiveSection == NavSection.MultiAsset;
    public bool IsCategoriesActive        => ActiveSection == NavSection.Categories;
    public bool IsRecurringActive         => ActiveSection == NavSection.Recurring;
    public bool IsReportsActive           => ActiveSection == NavSection.Reports;
    public bool IsTrendsActive            => ActiveSection == NavSection.Trends;
    public bool IsGoalsActive             => ActiveSection == NavSection.Goals;
    public bool IsAlertsActive            => ActiveSection == NavSection.Alerts;
    public bool IsImportActive            => ActiveSection == NavSection.Import;
    public bool IsRealEstateActive        => ActiveSection == NavSection.RealEstate;
    public bool IsInsuranceActive         => ActiveSection == NavSection.Insurance;
    public bool IsRetirementActive        => ActiveSection == NavSection.Retirement;
    public bool IsPhysicalAssetActive     => ActiveSection == NavSection.PhysicalAsset;
    public bool IsFireActive              => ActiveSection == NavSection.Fire;
    public bool IsMonteCarloActive        => ActiveSection == NavSection.MonteCarlo;
    public bool IsSettingsActive          => ActiveSection == NavSection.Settings;
}
