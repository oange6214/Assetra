using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;

namespace Assetra.WPF.Shell;

public partial class NavRailView : UserControl
{
    private NavRailViewModel? _navRail;
    private bool _serviceProviderWired;
    private bool _suppressSectionWriteBack;

    public NavRailView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_navRail is not null)
            _navRail.PropertyChanged -= OnNavRailPropertyChanged;

        if (e.NewValue is not MainViewModel main)
        {
            _navRail = null;
            return;
        }

        _navRail = main.NavRail;
        _navRail.PropertyChanged += OnNavRailPropertyChanged;

        // Hand the DI provider to NavigationView once so it can resolve Pages
        // through Wpf.Ui's TargetPageType navigation.
        if (!_serviceProviderWired)
        {
            RootNavView.SetServiceProvider(main.Services);
            _serviceProviderWired = true;
        }

        // Initial navigation: route to the Page matching the current ActiveSection.
        Dispatcher.BeginInvoke(() => NavigateToPageFor(_navRail.SelectedRailSection));
    }

    private void OnNavRailPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(NavRailViewModel.SelectedRailSection) || _navRail is null) return;
        Dispatcher.Invoke(() => NavigateToPageFor(_navRail.SelectedRailSection));
    }

    // SelectionChanged → user clicked a pane item. Wpf.Ui has already navigated
    // the Frame to TargetPageType; we just need to push the matching NavSection
    // back into NavRailViewModel so ActiveSection / history / hub-tab bindings
    // stay in sync.
    private void RootNavView_SelectionChanged(object sender, RoutedEventArgs e)
    {
        if (_navRail is null || _suppressSectionWriteBack) return;
        if (RootNavView.SelectedItem is not NavigationViewItem item) return;

        var section = SectionFor(item);
        if (section is null) return;

        // Avoid clobbering an already-correct section: if SelectedRailSection
        // already maps to this pane item, the user is on a leaf within a hub
        // (e.g. Categories under Cashflow) and shouldn't be reset to the hub.
        if (_navRail.SelectedRailSection == section.Value) return;

        _navRail.NavigateTo(section.Value);
    }

    private void NavigateToPageFor(NavSection section)
    {
        var item = FindNavItem(section);
        if (item?.TargetPageType is null) return;

        // Wpf.Ui auto-selects the matching item when we Navigate; suppress the
        // SelectionChanged write-back so we don't fight ourselves.
        _suppressSectionWriteBack = true;
        try
        {
            RootNavView.Navigate(item.TargetPageType);
        }
        finally
        {
            _suppressSectionWriteBack = false;
        }
    }

    private NavigationViewItem? FindNavItem(NavSection section) => section switch
    {
        NavSection.Portfolio         => NavPortfolio,
        NavSection.FinancialOverview => NavFinancialOverview,
        NavSection.Cashflow          => NavCashflow,
        NavSection.Insights          => NavInsights,
        NavSection.MultiAsset        => NavMultiAsset,
        NavSection.Alerts            => NavAlerts,
        NavSection.Import            => NavImport,
        NavSection.Settings          => NavSettings,
        _                            => null,
    };

    private NavSection? SectionFor(NavigationViewItem item)
    {
        if (ReferenceEquals(item, NavPortfolio))         return NavSection.Portfolio;
        if (ReferenceEquals(item, NavFinancialOverview)) return NavSection.FinancialOverview;
        if (ReferenceEquals(item, NavCashflow))          return NavSection.Cashflow;
        if (ReferenceEquals(item, NavInsights))          return NavSection.Insights;
        if (ReferenceEquals(item, NavMultiAsset))        return NavSection.MultiAsset;
        if (ReferenceEquals(item, NavAlerts))            return NavSection.Alerts;
        if (ReferenceEquals(item, NavImport))            return NavSection.Import;
        if (ReferenceEquals(item, NavSettings))          return NavSection.Settings;
        return null;
    }
}
