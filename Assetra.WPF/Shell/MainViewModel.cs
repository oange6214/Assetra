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

namespace Assetra.WPF.Shell;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly CompositeDisposable _disposables = new();

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
    public Features.PortfolioGroups.PortfolioGroupsViewModel PortfolioGroups { get; }
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
    public Features.Assistant.AssistantViewModel Assistant { get; }
    public Features.AuditLog.AuditLogViewModel AuditLog { get; }

    // Title bar search / command palette
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private bool _isSearchOpen;

    private readonly ObservableCollection<StockSearchResult> _searchResults = new();
    public ReadOnlyObservableCollection<StockSearchResult> SearchResults { get; }

    [RelayCommand]
    private void ToggleSearch() => IsSearchOpen = !IsSearchOpen;

    partial void OnSearchTextChanged(string value) => PerformSearch(value);

    private void PerformSearch(string query)
    {
        if (query.Length < 1)
        {
            _searchResults.Clear();
            return;
        }

        var fresh = _searchService.Search(query);

        // Remove items no longer in results
        for (var i = _searchResults.Count - 1; i >= 0; i--)
        {
            if (!fresh.Any(r => r.Symbol == _searchResults[i].Symbol))
                _searchResults.RemoveAt(i);
        }

        // Add new items not already present
        var existingSymbols = new HashSet<string>(_searchResults.Select(r => r.Symbol));
        foreach (var result in fresh)
        {
            if (!existingSymbols.Contains(result.Symbol))
                _searchResults.Add(result);
        }
    }

    // Clear the query and results whenever the popup closes — covers outside-click dismiss
    // (two-way bound from Popup.IsOpen) as well as the toggle command path.
    partial void OnIsSearchOpenChanged(bool value)
    {
        if (!value)
        {
            SearchText = string.Empty;
            _searchResults.Clear();
        }
    }

    // Navigation

    [RelayCommand]
    private void GoToSettings() => NavRail.ActiveSection = NavSection.Settings;

    // ── Title-bar「新增」dropdown menu commands ────────────────────────────
    // 5 quick-add entries that orchestrate "navigate to relevant page + open
    // dialog". 解決使用者「新增的資料去哪了」的困惑 — 點哪個項目就直接看到對應頁面開好 dialog。

    /// <summary>新增交易 — 開啟 transaction dialog（不切換頁面，dialog 是 modal）。</summary>
    [RelayCommand]
    private void AddTransactionFromMenu()
    {
        if (Portfolio.AddRecordCommand.CanExecute(null))
            Portfolio.AddRecordCommand.Execute(null);
    }

    // 「新增買入交易」menu 已移除（「新增交易」涵蓋同樣功能 — dialog 內可選類型）。
    // AddBuyTransactionFromMenuCommand 連同 lang key 也一併拿掉。

    /// <summary>新增資金帳戶 — 切到資金帳戶頁 + 開新增帳戶 dialog。</summary>
    [RelayCommand]
    private void AddAccountFromMenu()
    {
        NavRail.ActiveSection = NavSection.CashAccounts;
        if (Portfolio.OpenAddAccountDialogCommand.CanExecute(null))
            Portfolio.OpenAddAccountDialogCommand.Execute(null);
    }

    /// <summary>新增負債 — 切到負債頁 + 開新增負債 dialog。</summary>
    [RelayCommand]
    private void AddLiabilityFromMenu()
    {
        NavRail.ActiveSection = NavSection.Liabilities;
        if (Portfolio.OpenAddLiabilityDialogCommand.CanExecute(null))
            Portfolio.OpenAddLiabilityDialogCommand.Execute(null);
    }

    /// <summary>新增收支分類 — 切到收支分類頁 + 開新增分類 dialog。</summary>
    [RelayCommand]
    private void AddCategoryFromMenu()
    {
        NavRail.ActiveSection = NavSection.Categories;
        if (Categories.OpenAddCategoryCommand.CanExecute(null))
            Categories.OpenAddCategoryCommand.Execute(null);
    }

    /// <summary>新增訂閱排程 — 切到訂閱排程頁 + 開新增訂閱 dialog。</summary>
    [RelayCommand]
    private void AddRecurringFromMenu()
    {
        NavRail.ActiveSection = NavSection.Recurring;
        if (Recurring.OpenAddFormCommand.CanExecute(null))
            Recurring.OpenAddFormCommand.Execute(null);
    }

    /// <summary>新增警示 — 切到警示頁 + 開新增警示 dialog。</summary>
    [RelayCommand]
    private void AddAlertFromMenu()
    {
        NavRail.ActiveSection = NavSection.Alerts;
        if (Alerts.OpenAddFormCommand.CanExecute(null))
            Alerts.OpenAddFormCommand.Execute(null);
    }

    /// <summary>新增財務目標 — 切到財務目標頁 + 開新增目標 dialog。</summary>
    [RelayCommand]
    private void AddGoalFromMenu()
    {
        NavRail.ActiveSection = NavSection.Goals;
        if (Goals.OpenAddFormCommand.CanExecute(null))
            Goals.OpenAddFormCommand.Execute(null);
    }

    /// <summary>新增不動產 — 切到不動產頁 + 開新增不動產 dialog。</summary>
    [RelayCommand]
    private void AddRealEstateFromMenu()
    {
        NavRail.ActiveSection = NavSection.RealEstate;
        if (RealEstate.OpenAddFormCommand.CanExecute(null))
            RealEstate.OpenAddFormCommand.Execute(null);
    }

    /// <summary>新增保險保單 — 切到保險頁 + 開新增保單 dialog。</summary>
    [RelayCommand]
    private void AddInsuranceFromMenu()
    {
        NavRail.ActiveSection = NavSection.Insurance;
        if (Insurance.OpenAddFormCommand.CanExecute(null))
            Insurance.OpenAddFormCommand.Execute(null);
    }

    /// <summary>新增退休專戶 — 切到退休專戶頁 + 開新增專戶 dialog。</summary>
    [RelayCommand]
    private void AddRetirementFromMenu()
    {
        NavRail.ActiveSection = NavSection.Retirement;
        if (Retirement.OpenAddFormCommand.CanExecute(null))
            Retirement.OpenAddFormCommand.Execute(null);
    }

    /// <summary>新增實物資產 — 切到實物資產頁 + 開新增資產 dialog。</summary>
    [RelayCommand]
    private void AddPhysicalAssetFromMenu()
    {
        NavRail.ActiveSection = NavSection.PhysicalAsset;
        if (PhysicalAsset.OpenAddFormCommand.CanExecute(null))
            PhysicalAsset.OpenAddFormCommand.Execute(null);
    }

    // Theme

    private readonly IStockSearchService _searchService;
    private readonly IThemeService _themeService;

    [ObservableProperty] private ApplicationTheme _currentTheme;

    public string ThemeToggleLabel => CurrentTheme == ApplicationTheme.Dark ? "淺色" : "深色";

    partial void OnCurrentThemeChanged(ApplicationTheme value)
    {
        // Title-bar 已用 DataTrigger 在 XAML 直接切換 ds:AppIcon Symbol，
        // 不再需要回傳 emoji 字串給 binding。
        OnPropertyChanged(nameof(ThemeToggleLabel));
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
        DashboardViewModel dashboard,
        FinancialOverviewViewModel financialOverview,
        AlertsViewModel alerts,
        CategoriesViewModel categories,
        RecurringViewModel recurring,
        ReportsViewModel reports,
        GoalsViewModel goals,
        Features.PortfolioGroups.PortfolioGroupsViewModel portfolioGroups,
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
        Features.Assistant.AssistantViewModel assistant,
        Features.AuditLog.AuditLogViewModel auditLog,
        IThemeService themeService,
        IStockSearchService searchService,
        ILocalizationService localization)
    {
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
        PortfolioGroups = portfolioGroups;
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
        Assistant = assistant;
        AuditLog = auditLog;
        _themeService = themeService;
        _searchService = searchService;
        CurrentTheme = themeService.CurrentTheme;
        SearchResults = new ReadOnlyObservableCollection<StockSearchResult>(_searchResults);

        Portfolio.AttachTabViewModels(Dashboard, Allocation);

        // P2.12 — Command Palette (Ctrl+Shift+K) seed must happen after sub-VMs are
        // assigned (lambdas capture them).
        InitializeCommandPalette(localization);

        // P2.13 — 把今日 P&L % 推進 StatusBar 顯示。Portfolio 是真正的 DayPnl
        // owner，StatusBar 不該知道 Portfolio 結構，所以這裡用 PropertyChanged 橋接。
        Portfolio.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(PortfolioViewModel.DayPnlPercentDisplay)
                                or nameof(PortfolioViewModel.HasDayPnl)
                                or nameof(PortfolioViewModel.IsDayPnlPositive))
                UpdateStatusBarTodayReturn();
        };
        UpdateStatusBarTodayReturn();
    }

    /// <summary>
    /// P2.13 — 從 PortfolioVM 的 DayPnlPercentDisplay 取「(+1.23%)」 strip 掉
    /// 括號，套上「今日」前綴 (i18n) 推給 StatusBar.TodayReturnText。
    /// </summary>
    private void UpdateStatusBarTodayReturn()
    {
        if (!Portfolio.HasDayPnl)
        {
            StatusBar.TodayReturnText = string.Empty;
            return;
        }
        var raw = Portfolio.DayPnlPercentDisplay; // e.g. "(+1.23%)" or "(-0.45%)"
        var stripped = raw.Trim('(', ')', ' ');
        var prefix = LocalizationFallback("StatusBar.TodayReturnPrefix", "今日");
        StatusBar.TodayReturnText = $"{prefix} {stripped}";
        StatusBar.IsTodayReturnPositive = Portfolio.IsDayPnlPositive;
    }

    private static string LocalizationFallback(string key, string fallback)
    {
        try
        {
            if (System.Windows.Application.Current?.TryFindResource(key) is string s)
                return s;
        }
        catch { /* ignore */ }
        return fallback;
    }

    public void Dispose()
    {
        WeakReferenceMessenger.Default.UnregisterAll(this);
        _disposables.Dispose();
    }
}
