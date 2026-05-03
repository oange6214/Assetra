using System.Windows;
using System.Windows.Controls;
using Assetra.WPF.Features.Alerts;
using Assetra.WPF.Features.FinancialOverview;
using Assetra.WPF.Features.Hubs;
using Assetra.WPF.Features.Import;
using Assetra.WPF.Features.Portfolio;
using Assetra.WPF.Features.Reconciliation;
using Assetra.WPF.Features.Settings;

namespace Assetra.WPF.Shell;

// Page wrappers consumed by ui:NavigationView via TargetPageType.
// Each Page sets DataContext to the right VM (or MainViewModel for hubs that
// need cross-VM bindings) and hosts the existing UserControl unchanged.
//
// Pages are registered as singletons in DI; Wpf.Ui's NavigationView resolves
// them through SetServiceProvider so navigation reuses one instance per Page.

public sealed class PortfolioPage : Page
{
    public PortfolioPage(MainViewModel main)
    {
        DataContext = main.Portfolio;
        Content = new PortfolioView();
    }
}

public sealed class FinancialOverviewPage : Page
{
    public FinancialOverviewPage(MainViewModel main)
    {
        DataContext = main.FinancialOverview;
        Content = new FinancialOverviewView();
    }
}

public sealed class CashflowHubPage : Page
{
    public CashflowHubPage(MainViewModel main)
    {
        DataContext = main;
        Content = new CashflowHubView();
    }
}

public sealed class InsightsHubPage : Page
{
    public InsightsHubPage(MainViewModel main)
    {
        DataContext = main;
        Content = new InsightsHubView();
    }
}

public sealed class MultiAssetHubPage : Page
{
    public MultiAssetHubPage(MainViewModel main)
    {
        DataContext = main;
        Content = new MultiAssetHubView();
    }
}

public sealed class AlertsPage : Page
{
    public AlertsPage(MainViewModel main)
    {
        DataContext = main.Alerts;
        Content = new AlertsView();
    }
}

public sealed class ImportPage : Page
{
    public ImportPage(MainViewModel main)
    {
        DataContext = main;

        var tabs = new TabControl
        {
            Background = System.Windows.Media.Brushes.Transparent,
            BorderThickness = default,
        };

        tabs.Items.Add(MakeTab("Import.Tab.Import", new ImportView { DataContext = main.Import }));
        tabs.Items.Add(MakeTab("Reconciliation.Tab", new ReconciliationView { DataContext = main.Reconciliation }));

        Content = tabs;

        static TabItem MakeTab(string headerResourceKey, FrameworkElement content)
        {
            var tab = new TabItem { Content = content };
            tab.SetResourceReference(HeaderedContentControl.HeaderProperty, headerResourceKey);
            return tab;
        }
    }
}

public sealed class SettingsPage : Page
{
    public SettingsPage(MainViewModel main)
    {
        DataContext = main.Settings;
        Content = new SettingsView();
    }
}
