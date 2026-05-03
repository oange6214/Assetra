using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;

namespace Assetra.WPF.Shell;

public partial class NavRailView : UserControl
{
    private NavRailViewModel? _navRail;
    // Track active item ourselves: NavigationView only updates its SelectedItem
    // from its own internal click path (which we bypass to dodge the Frame.Navigate
    // throw on null TargetPageType). Without this, Activate() calls would stack red
    // indicator bars instead of switching them.
    private NavigationViewItem? _activeItem;

    public NavRailView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_navRail is not null)
            _navRail.PropertyChanged -= OnNavRailPropertyChanged;

        _navRail = (e.NewValue as MainViewModel)?.NavRail;
        if (_navRail is null) return;

        _navRail.PropertyChanged += OnNavRailPropertyChanged;
        Dispatcher.BeginInvoke(() => SyncNavViewSelection(_navRail.ActiveSection));
    }

    // Listen to ActiveSection (leaf-level) — not SelectedRailSection (hub-level) —
    // so a leaf inside a group correctly highlights the leaf item, not its parent.
    private void OnNavRailPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(NavRailViewModel.ActiveSection) || _navRail is null) return;
        Dispatcher.Invoke(() => SyncNavViewSelection(_navRail.ActiveSection));
    }

    private void SyncNavViewSelection(NavSection section)
    {
        var target = FindNavItem(section);
        if (target is null || ReferenceEquals(_activeItem, target)) return;

        _activeItem?.Deactivate(RootNavView);
        target.Activate(RootNavView);
        _activeItem = target;
    }

    private NavigationViewItem? FindNavItem(NavSection section) => section switch
    {
        NavSection.Portfolio         => NavPortfolio,
        NavSection.FinancialOverview => NavFinancialOverview,
        NavSection.Categories        => NavCategories,
        NavSection.Recurring         => NavRecurring,
        NavSection.Goals             => NavGoals,
        NavSection.Trends            => NavTrends,
        NavSection.Reports           => NavReports,
        NavSection.Fire              => NavFire,
        NavSection.MonteCarlo        => NavMonteCarlo,
        NavSection.RealEstate        => NavRealEstate,
        NavSection.Insurance         => NavInsurance,
        NavSection.Retirement        => NavRetirement,
        NavSection.PhysicalAsset     => NavPhysicalAsset,
        NavSection.Alerts            => NavAlerts,
        NavSection.Import            => NavImport,
        NavSection.Settings          => NavSettings,
        // Cashflow / Insights / MultiAsset are parent-only items in the pane;
        // ActiveSection is normalized to a leaf, so these cases never fire.
        _                            => null,
    };

    // Drive ActiveSection from per-item Click rather than NavigationView.SelectionChanged.
    // Wpf.Ui's internal click handler tries to navigate the Frame and throws when
    // TargetPageType is null, which we don't use (DataTemplate routing instead).
    // Parent items have no TargetPageTag — clicks fall through and let
    // NavigationView's built-in expand/collapse handle them.
    private void NavItem_Click(object sender, RoutedEventArgs e)
    {
        if (_navRail is null) return;
        if (sender is NavigationViewItem { TargetPageTag: { } tag } &&
            Enum.TryParse<NavSection>(tag, out var section))
        {
            _navRail.NavigateTo(section);
        }
    }
}
