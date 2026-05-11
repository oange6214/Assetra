using Assetra.Core.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Assetra.WPF.Shell;

public partial class NavRailViewModel : ObservableObject
{
    private readonly Stack<NavSection> _backStack = new();
    private readonly Stack<NavSection> _forwardStack = new();
    private readonly ILocalizationService? _localization;
    private bool _isHistoryNavigation;

    /// <summary>Parameterless ctor preserved for tests + design-time.</summary>
    public NavRailViewModel()
    {
        Groups = BuildGroups();
        BottomItems = BuildBottomItems();
        SyncActiveLeaf();
        RefreshLocalizedLabels();
    }

    /// <summary>
    /// DI-friendly constructor. Initial section is read from
    /// <see cref="AppSettings.DefaultHomeSection"/> so each user can pin
    /// their preferred startup landing page (default = FinancialOverview).
    /// Localization service is optional — when present, group + leaf labels
    /// refresh automatically on language change.
    /// </summary>
    public NavRailViewModel(IAppSettingsService settings, ILocalizationService? localization = null)
    {
        _activeSection = ResolveInitialSection(settings.Current.DefaultHomeSection);
        _localization = localization;
        Groups = BuildGroups();
        BottomItems = BuildBottomItems();
        SyncActiveLeaf();
        RefreshLocalizedLabels();
        if (localization is not null)
            localization.LanguageChanged += (_, _) => RefreshLocalizedLabels();

        // Subscribe to shell-level navigation requests (e.g. transaction-dialog
        // 「查看交易」snackbar action). Lifetime = application; no unsubscribe needed.
        Assetra.WPF.Infrastructure.ShellNavigationEvents.NavigationRequested += NavigateToByName;
    }

    private static NavSection ResolveInitialSection(string raw)
    {
        if (!string.IsNullOrWhiteSpace(raw)
            && Enum.TryParse<NavSection>(raw, ignoreCase: true, out var parsed))
            return parsed;
        return NavSection.FinancialOverview;
    }

    // ─── Data-driven nav structure (D 方案: collapsible groups + flyout) ───

    /// <summary>Top-level groups shown in the middle scrollable region.</summary>
    public IReadOnlyList<NavGroupVm> Groups { get; }

    /// <summary>Always-visible items pinned at the rail bottom (Import, Settings).</summary>
    public IReadOnlyList<NavLeafVm> BottomItems { get; }

    private IReadOnlyList<NavGroupVm> BuildGroups()
    {
        return new[]
        {
            new NavGroupVm
            {
                TitleResourceKey = "Nav.Overview",
                GroupIconSymbol = "Apps24",
                Items = new[]
                {
                    new NavLeafVm { Section = NavSection.FinancialOverview, LabelResourceKey = "FinancialOverview.Nav.Label", IconSymbol = "DataPie24",            ToolTipResourceKey = "FinancialOverview.Nav.Label" },
                    // Stage 2 (Dashboard consolidation)：資產趨勢併入財務儀表板的「資產趨勢」tab；
                    // NavSection.Trends enum 保留以兼容舊持久化設定，NavigateTo() 會攔截重導。
                    new NavLeafVm { Section = NavSection.Reports,           LabelResourceKey = "Nav.Reports",                 IconSymbol = "DocumentBulletList24", ToolTipResourceKey = "Nav.Reports" },
                    new NavLeafVm { Section = NavSection.Assistant,         LabelResourceKey = "Nav.Assistant",               IconSymbol = "Sparkle24",            ToolTipResourceKey = "Nav.Assistant" },
                    new NavLeafVm { Section = NavSection.AuditLog,          LabelResourceKey = "Nav.AuditLog",                IconSymbol = "History24",            ToolTipResourceKey = "Nav.AuditLog" },
                },
            },
            new NavGroupVm
            {
                TitleResourceKey = "Nav.Assets",
                GroupIconSymbol = "Wallet24",
                Items = new[]
                {
                    new NavLeafVm { Section = NavSection.Portfolio,     LabelResourceKey = "Nav.Portfolio",     IconSymbol = "Briefcase24",   ToolTipResourceKey = "Nav.Portfolio" },
                    new NavLeafVm { Section = NavSection.CashAccounts,  LabelResourceKey = "Nav.CashAccounts",  IconSymbol = "Money24",       ToolTipResourceKey = "Nav.CashAccounts" },
                    new NavLeafVm { Section = NavSection.Liabilities,   LabelResourceKey = "Nav.Liabilities",   IconSymbol = "Cut24",         ToolTipResourceKey = "Nav.Liabilities" },
                    new NavLeafVm { Section = NavSection.RealEstate,    LabelResourceKey = "RealEstate.Title",  IconSymbol = "Home24",        ToolTipResourceKey = "RealEstate.Title" },
                    new NavLeafVm { Section = NavSection.Insurance,     LabelResourceKey = "Insurance.Title",   IconSymbol = "Shield24",      ToolTipResourceKey = "Insurance.Title" },
                    new NavLeafVm { Section = NavSection.Retirement,    LabelResourceKey = "Retirement.Title",  IconSymbol = "PersonClock24", ToolTipResourceKey = "Retirement.Title" },
                    new NavLeafVm { Section = NavSection.PhysicalAsset, LabelResourceKey = "PhysicalAsset.Title", IconSymbol = "Box24",       ToolTipResourceKey = "PhysicalAsset.Title" },
                },
            },
            new NavGroupVm
            {
                TitleResourceKey = "Nav.Cashflow",
                GroupIconSymbol = "ArrowSwap24",
                Items = new[]
                {
                    new NavLeafVm { Section = NavSection.Categories,     LabelResourceKey = "Nav.Categories",     IconSymbol = "Tag24",                ToolTipResourceKey = "Nav.Categories" },
                    new NavLeafVm { Section = NavSection.Recurring,      LabelResourceKey = "Nav.Recurring",      IconSymbol = "CalendarLtr24",        ToolTipResourceKey = "Nav.Recurring",      BadgeKind = "Recurring" },
                    new NavLeafVm { Section = NavSection.TransactionLog, LabelResourceKey = "Nav.TransactionLog", IconSymbol = "DocumentBulletList24", ToolTipResourceKey = "Nav.TransactionLog" },
                    new NavLeafVm { Section = NavSection.Alerts,         LabelResourceKey = "Nav.Alerts",         IconSymbol = "Alert24",              ToolTipResourceKey = "Nav.Alerts.Tooltip", BadgeKind = "Alerts" },
                },
            },
            new NavGroupVm
            {
                TitleResourceKey = "Nav.Planning",
                GroupIconSymbol = "Compass24",
                Items = new[]
                {
                    new NavLeafVm { Section = NavSection.Goals,      LabelResourceKey = "Nav.Goals",       IconSymbol = "Target24",     ToolTipResourceKey = "Nav.Goals" },
                    new NavLeafVm { Section = NavSection.Fire,       LabelResourceKey = "Fire.Title",      IconSymbol = "Flash24",      ToolTipResourceKey = "Fire.Title" },
                    new NavLeafVm { Section = NavSection.MonteCarlo, LabelResourceKey = "MonteCarlo.Title", IconSymbol = "Calculator24", ToolTipResourceKey = "MonteCarlo.Title" },
                },
            },
        };
    }

    private IReadOnlyList<NavLeafVm> BuildBottomItems() => new[]
    {
        new NavLeafVm { Section = NavSection.Import,   LabelResourceKey = "Nav.Import",   IconSymbol = "DocumentArrowDown24", ToolTipResourceKey = "Nav.Import" },
        new NavLeafVm { Section = NavSection.Settings, LabelResourceKey = "Nav.Settings", IconSymbol = "Settings24",          ToolTipResourceKey = "Nav.Settings" },
    };

    private void SyncActiveLeaf()
    {
        foreach (var g in Groups)
        {
            var anyActive = false;
            foreach (var leaf in g.Items)
            {
                var isActive = leaf.Section == _activeSection;
                leaf.IsActive = isActive;
                if (isActive) anyActive = true;
            }
            g.HasActiveChild = anyActive;
            // Auto-expand the group containing the active section so the
            // current page stays visible after navigation.
            if (anyActive) g.IsExpanded = true;
        }
        foreach (var leaf in BottomItems)
            leaf.IsActive = leaf.Section == _activeSection;
    }

    private void RefreshLocalizedLabels()
    {
        foreach (var g in Groups)
        {
            g.LocalizedTitle = ResolveString(g.TitleResourceKey);
            foreach (var leaf in g.Items)
            {
                leaf.LocalizedLabel = ResolveString(leaf.LabelResourceKey);
                leaf.LocalizedToolTip = ResolveString(leaf.ToolTipResourceKey);
            }
        }
        foreach (var leaf in BottomItems)
        {
            leaf.LocalizedLabel = ResolveString(leaf.LabelResourceKey);
            leaf.LocalizedToolTip = ResolveString(leaf.ToolTipResourceKey);
        }
    }

    private string ResolveString(string key)
    {
        if (_localization is not null)
            return _localization.Get(key, key);
        // Fallback: try Application.Current resources (works at runtime even
        // without the service); returns the key itself if not found, which
        // matches the .NET resource convention.
        try
        {
            var app = System.Windows.Application.Current;
            var value = app?.TryFindResource(key) as string;
            return value ?? key;
        }
        catch
        {
            return key;
        }
    }

    // ─── Existing navigation API (unchanged contracts) ───

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
        // Stage 2 (Dashboard consolidation)：舊 NavSection.Trends 已併入財務儀表板
        // 的「資產趨勢」tab。攔截重導 + 通知 FinancialOverview 切到該 tab；
        // 兼容外部 deep link 與舊持久化設定（DefaultHomeSection = "Trends"）。
        if (section == NavSection.Trends)
        {
            section = NavSection.FinancialOverview;
            Assetra.WPF.Infrastructure.ShellNavigationEvents.RequestDashboardTab(
                nameof(Assetra.WPF.Features.FinancialOverview.DashboardTab.Trends));
        }

        if (section == _activeSection) return;

        if (!_isHistoryNavigation)
        {
            _backStack.Push(_activeSection);
            _forwardStack.Clear();
        }

        SetProperty(ref _activeSection, section, nameof(ActiveSection));
        // Also close any open flyout once a leaf is selected.
        if (Groups is not null)
            foreach (var g in Groups) g.IsFlyoutOpen = false;
        SyncActiveLeaf();
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
        GoBackCommand.NotifyCanExecuteChanged();
        GoForwardCommand.NotifyCanExecuteChanged();
    }
}
