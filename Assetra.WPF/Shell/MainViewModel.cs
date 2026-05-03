using System.Collections.ObjectModel;
using System.Reactive.Disposables;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Assetra.WPF.Features.Alerts;
using Assetra.WPF.Features.Categories;
using Assetra.WPF.Features.FinancialOverview;
using Assetra.WPF.Features.Fire;
using Assetra.WPF.Features.Goals;
using Assetra.WPF.Features.Import;
using Assetra.WPF.Features.Insurance;
using Assetra.WPF.Features.MonteCarlo;
using Assetra.WPF.Features.PhysicalAsset;
using Assetra.WPF.Features.Reconciliation;
using Assetra.WPF.Features.Recurring;
using Assetra.WPF.Features.Reports;
using Assetra.WPF.Features.Portfolio.Controls;
using Assetra.WPF.Features.Portfolio;
using Assetra.WPF.Features.RealEstate;
using Assetra.WPF.Features.Retirement;
using Assetra.WPF.Features.Settings;
using Assetra.WPF.Features.Snackbar;
using Assetra.WPF.Features.StatusBar;
using Assetra.WPF.Infrastructure;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Wpf.Ui.Appearance;

namespace Assetra.WPF.Shell;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly CompositeDisposable _disposables = new();

    // Exposed so NavRailView can hand this to ui:NavigationView.SetServiceProvider,
    // letting Wpf.Ui resolve Page instances from DI on Navigate(typeof(...)).
    public IServiceProvider Services { get; }

    public NavRailViewModel NavRail { get; }
    public StatusBarViewModel StatusBar { get; }
    public PortfolioViewModel Portfolio { get; }
    public AllocationViewModel Allocation { get; }
    public DashboardViewModel Dashboard { get; }
    public FinancialOverviewViewModel FinancialOverview { get; }
    public AlertsViewModel Alerts { get; }
    public CategoriesViewModel Categories { get; }
    public RecurringViewModel Recurring { get; }
    public ReportsViewModel Reports { get; }
    public GoalsViewModel Goals { get; }
    public ImportViewModel Import { get; }
    public ReconciliationViewModel Reconciliation { get; }
    public RealEstateViewModel RealEstate { get; }
    public InsurancePolicyViewModel Insurance { get; }
    public RetirementViewModel Retirement { get; }
    public PhysicalAssetViewModel PhysicalAsset { get; }
    public FireViewModel Fire { get; }
    public MonteCarloViewModel MonteCarlo { get; }
    public SettingsViewModel Settings { get; }
    public SnackbarViewModel Snackbar { get; }

    // Title bar search / command palette
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private bool _isSearchOpen;

    public ObservableCollection<StockSearchResult> SearchResults { get; } = new();

    [RelayCommand]
    private void ToggleSearch() => IsSearchOpen = !IsSearchOpen;

    partial void OnSearchTextChanged(string value) => PerformSearch(value);

    private void PerformSearch(string query)
    {
        if (query.Length < 1)
        {
            SearchResults.Clear();
            return;
        }

        var fresh = _searchService.Search(query);

        // Remove items no longer in results
        for (var i = SearchResults.Count - 1; i >= 0; i--)
        {
            if (!fresh.Any(r => r.Symbol == SearchResults[i].Symbol))
                SearchResults.RemoveAt(i);
        }

        // Add new items not already present
        var existingSymbols = new HashSet<string>(SearchResults.Select(r => r.Symbol));
        foreach (var result in fresh)
        {
            if (!existingSymbols.Contains(result.Symbol))
                SearchResults.Add(result);
        }
    }

    // Clear the query and results whenever the popup closes — covers outside-click dismiss
    // (two-way bound from Popup.IsOpen) as well as the toggle command path.
    partial void OnIsSearchOpenChanged(bool value)
    {
        if (!value)
        {
            SearchText = string.Empty;
            SearchResults.Clear();
        }
    }

    // Navigation

    [RelayCommand]
    private void GoToSettings() => NavRail.ActiveSection = NavSection.Settings;

    // Theme

    private readonly IStockSearchService _searchService;
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
        IServiceProvider services,
        NavRailViewModel navRail,
        StatusBarViewModel statusBar,
        PortfolioViewModel portfolio,
        AllocationViewModel allocation,
        DashboardViewModel dashboard,
        FinancialOverviewViewModel financialOverview,
        AlertsViewModel alerts,
        CategoriesViewModel categories,
        RecurringViewModel recurring,
        ReportsViewModel reports,
        GoalsViewModel goals,
        ImportViewModel import,
        ReconciliationViewModel reconciliation,
        RealEstateViewModel realEstate,
        InsurancePolicyViewModel insurance,
        RetirementViewModel retirement,
        PhysicalAssetViewModel physicalAsset,
        FireViewModel fire,
        MonteCarloViewModel monteCarlo,
        SettingsViewModel settings,
        SnackbarViewModel snackbar,
        IThemeService themeService,
        IStockSearchService searchService)
    {
        Services = services;
        NavRail = navRail;
        StatusBar = statusBar;
        Portfolio = portfolio;
        Allocation = allocation;
        Dashboard = dashboard;
        FinancialOverview = financialOverview;
        Alerts = alerts;
        Categories = categories;
        Recurring = recurring;
        Reports = reports;
        Goals = goals;
        Import = import;
        Reconciliation = reconciliation;
        RealEstate = realEstate;
        Insurance = insurance;
        Retirement = retirement;
        PhysicalAsset = physicalAsset;
        Fire = fire;
        MonteCarlo = monteCarlo;
        Settings = settings;
        Snackbar = snackbar;
        _themeService = themeService;
        _searchService = searchService;
        CurrentTheme = themeService.CurrentTheme;
    }

    public void Dispose()
    {
        WeakReferenceMessenger.Default.UnregisterAll(this);
        _disposables.Dispose();
    }
}
