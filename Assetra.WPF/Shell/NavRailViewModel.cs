using System.ComponentModel;
using Assetra.Core.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Assetra.WPF.Shell;

public partial class NavRailViewModel : ObservableObject
{
    private readonly Stack<NavSection> _backStack = new();
    private readonly Stack<NavSection> _forwardStack = new();
    private readonly ILocalizationService? _localization;
    private readonly IAppSettingsService? _settings;
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
        _settings = settings;
        _activeSection = ResolveInitialSection(settings.Current.DefaultHomeSection);
        _localization = localization;
        Groups = BuildGroups();
        BottomItems = BuildBottomItems();
        RestoreGroupExpansion();
        SyncActiveLeaf();
        RefreshLocalizedLabels();
        // Subscribe AFTER restore + SyncActiveLeaf so neither the persisted-state
        // restore nor the initial active-group auto-expand feed back as saves.
        SubscribeGroupExpansionPersistence();
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
            // 「分析」群組：日常觀察與輸出（語意收斂；原 Nav.Overview 拆出）。
            new NavGroupVm
            {
                TitleResourceKey = "Nav.Analysis",
                GroupIconSymbol = "DataPie24",
                IsExpanded = true,
                Items = new[]
                {
                    new NavLeafVm { Section = NavSection.FinancialOverview, LabelResourceKey = "FinancialOverview.Nav.Label", IconSymbol = "DataPie24",            ToolTipResourceKey = "Nav.FinancialOverview.Tip" },
                    // NavSection.Trends 已併入財務概覽的「資產趨勢」tab；
                    // NavigateTo() 攔截舊持久化重導。
                    new NavLeafVm { Section = NavSection.Reports,           LabelResourceKey = "Nav.Reports",                 IconSymbol = "DocumentBulletList24", ToolTipResourceKey = "Nav.Reports.Tip" },
                    new NavLeafVm { Section = NavSection.Assistant,         LabelResourceKey = "Nav.Assistant",               IconSymbol = "Sparkle24",            ToolTipResourceKey = "Nav.Assistant.Tip" },
                },
            },
            // 「資產」核心群組：新手最常用的三類（投資 / 現金 / 負債），預設展開。
            new NavGroupVm
            {
                TitleResourceKey = "Nav.Assets",
                GroupIconSymbol = "Wallet24",
                IsExpanded = true,
                Items = new[]
                {
                    new NavLeafVm { Section = NavSection.Portfolio,     LabelResourceKey = "Nav.Portfolio",     IconSymbol = "Briefcase24",   ToolTipResourceKey = "Nav.Portfolio.Tip" },
                    new NavLeafVm { Section = NavSection.CashAccounts,  LabelResourceKey = "Nav.CashAccounts",  IconSymbol = "Money24",       ToolTipResourceKey = "Nav.CashAccounts.Tip" },
                    new NavLeafVm { Section = NavSection.Liabilities,   LabelResourceKey = "Nav.Liabilities",   IconSymbol = "Cut24",         ToolTipResourceKey = "Nav.Liabilities.Tip" },
                },
            },
            new NavGroupVm
            {
                TitleResourceKey = "Nav.Cashflow",
                GroupIconSymbol = "ArrowSwap24",
                IsExpanded = true,
                Items = new[]
                {
                    new NavLeafVm { Section = NavSection.Categories,     LabelResourceKey = "Nav.Categories",     IconSymbol = "Tag24",                ToolTipResourceKey = "Nav.Categories.Tip" },
                    new NavLeafVm { Section = NavSection.Recurring,      LabelResourceKey = "Nav.Recurring",      IconSymbol = "CalendarLtr24",        ToolTipResourceKey = "Nav.Recurring.Tip",      BadgeKind = "Recurring" },
                    new NavLeafVm { Section = NavSection.TransactionLog, LabelResourceKey = "Nav.TransactionLog", IconSymbol = "DocumentBulletList24", ToolTipResourceKey = "Nav.TransactionLog.Tip" },
                    new NavLeafVm { Section = NavSection.Alerts,         LabelResourceKey = "Nav.Alerts",         IconSymbol = "Alert24",              ToolTipResourceKey = "Nav.Alerts.Tip", BadgeKind = "Alerts" },
                },
            },
            // 「其他資產」群組：進階資產類別（不動產 / 保險 / 退休 / 實物），
            // 預設收合，避免首次使用者被大量選項淹沒。
            new NavGroupVm
            {
                TitleResourceKey = "Nav.MoreAssets",
                GroupIconSymbol = "Wallet24",
                IsExpanded = false,
                Items = new[]
                {
                    new NavLeafVm { Section = NavSection.RealEstate,    LabelResourceKey = "RealEstate.Title",  IconSymbol = "Home24",        ToolTipResourceKey = "Nav.RealEstate.Tip" },
                    new NavLeafVm { Section = NavSection.Insurance,     LabelResourceKey = "Insurance.Title",   IconSymbol = "Shield24",      ToolTipResourceKey = "Nav.Insurance.Tip" },
                    new NavLeafVm { Section = NavSection.Retirement,    LabelResourceKey = "Retirement.Title",  IconSymbol = "PersonClock24", ToolTipResourceKey = "Nav.Retirement.Tip" },
                    new NavLeafVm { Section = NavSection.PhysicalAsset, LabelResourceKey = "PhysicalAsset.Title", IconSymbol = "Box24",       ToolTipResourceKey = "Nav.PhysicalAsset.Tip" },
                },
            },
            new NavGroupVm
            {
                TitleResourceKey = "Nav.Planning",
                // P5.1 — Compass24 在 user 螢幕字型下視覺接近 Wrench24（剛好下面就是
                // 工具群組用 Wrench24，兩個 icon 看起來會混淆）。換 Rocket24 強調
                // 「規劃 / 前瞻」語意，跟工具的扳手清楚區隔。
                GroupIconSymbol = "Rocket24",
                IsExpanded = false,
                Items = new[]
                {
                    new NavLeafVm { Section = NavSection.Goals,      LabelResourceKey = "Nav.Goals",       IconSymbol = "Target24",     ToolTipResourceKey = "Nav.Goals.Tip" },
                    new NavLeafVm { Section = NavSection.Fire,       LabelResourceKey = "Fire.Title",      IconSymbol = "Flash24",      ToolTipResourceKey = "Nav.Fire.Tip" },
                    new NavLeafVm { Section = NavSection.MonteCarlo, LabelResourceKey = "MonteCarlo.Title", IconSymbol = "Calculator24", ToolTipResourceKey = "Nav.MonteCarlo.Tip" },
                    new NavLeafVm { Section = NavSection.Calculators, LabelResourceKey = "Calc.Title", IconSymbol = "Calculator24", ToolTipResourceKey = "Nav.Calculators.Tip" },
                },
            },
            // 「工具」群組：診斷類項目（拆自 Nav.Overview），與日常分析語意分開。
            new NavGroupVm
            {
                TitleResourceKey = "Nav.Tools",
                GroupIconSymbol = "Wrench24",
                IsExpanded = false,
                Items = new[]
                {
                    new NavLeafVm { Section = NavSection.AuditLog, LabelResourceKey = "Nav.AuditLog", IconSymbol = "History24", ToolTipResourceKey = "Nav.AuditLog.Tip" },
                },
            },
        };
    }

    private IReadOnlyList<NavLeafVm> BuildBottomItems() => new[]
    {
        new NavLeafVm { Section = NavSection.Import,   LabelResourceKey = "Nav.Import",   IconSymbol = "DocumentArrowDown24", ToolTipResourceKey = "Nav.Import.Tip" },
        new NavLeafVm { Section = NavSection.Settings, LabelResourceKey = "Nav.Settings", IconSymbol = "Settings24",          ToolTipResourceKey = "Nav.Settings.Tip" },
    };

    // ─── Group expansion persistence (remembers expand/collapse per user) ───

    /// <summary>
    /// Applies the persisted <see cref="AppSettings.NavExpandedGroups"/> set onto the
    /// freshly-built groups. Empty setting = leave the BuildGroups() code defaults
    /// untouched (nothing UI-visible changes on first run). Only called from the DI ctor.
    /// </summary>
    private void RestoreGroupExpansion()
    {
        var raw = _settings?.Current.NavExpandedGroups;
        if (string.IsNullOrEmpty(raw))
            return;
        var expanded = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                          .ToHashSet();
        foreach (var g in Groups)
            g.IsExpanded = expanded.Contains(g.TitleResourceKey);
    }

    /// <summary>
    /// Subscribes to each group's PropertyChanged so a later expand/collapse recomputes
    /// and persists the expanded-set string. Must run AFTER RestoreGroupExpansion() +
    /// SyncActiveLeaf() so construction-time expansion changes don't self-trigger saves.
    /// </summary>
    private void SubscribeGroupExpansionPersistence()
    {
        if (_settings is null)
            return;
        foreach (var g in Groups)
            g.PropertyChanged += OnGroupPropertyChanged;
    }

    private void OnGroupPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(NavGroupVm.IsExpanded))
            return;
        PersistGroupExpansion();
    }

    /// <summary>
    /// Recomputes the comma-joined expanded-group set and persists it. raiseChanged: false
    /// —— pure UI bookkeeping, must NOT drive the app-wide IAppSettingsService.Changed
    /// reload (見 settings-changed 回饋迴圈). Mirrors PortfolioViewModel.PersistUiPreferenceAsync.
    /// </summary>
    private void PersistGroupExpansion()
    {
        if (_settings is null)
            return;
        var expanded = string.Join(',', Groups.Where(g => g.IsExpanded).Select(g => g.TitleResourceKey));
        _ = PersistExpandedSetAsync(expanded);
    }

    private async Task PersistExpandedSetAsync(string expanded)
    {
        try
        {
            await _settings!.SaveAsync(
                _settings.Current with { NavExpandedGroups = expanded },
                raiseChanged: false);
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "[Nav] 持久化群組展開偏好失敗");
        }
    }

    private void SyncActiveLeaf()
    {
        foreach (var g in Groups)
        {
            var anyActive = false;
            foreach (var leaf in g.Items)
            {
                var isActive = leaf.Section == _activeSection;
                leaf.IsActive = isActive;
                if (isActive)
                    anyActive = true;
            }
            g.HasActiveChild = anyActive;
            // Auto-expand the group containing the active section so the
            // current page stays visible after navigation.
            if (anyActive)
                g.IsExpanded = true;
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

    public bool CanGoBack => _backStack.Count > 0;
    public bool CanGoForward => _forwardStack.Count > 0;

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void GoBack()
    {
        if (!_backStack.TryPop(out var prev))
            return;
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
        if (!_forwardStack.TryPop(out var next))
            return;
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
        if (string.IsNullOrWhiteSpace(name))
            return;
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

        if (section == _activeSection)
            return;

        if (!_isHistoryNavigation)
        {
            _backStack.Push(_activeSection);
            _forwardStack.Clear();
        }

        SetProperty(ref _activeSection, section, nameof(ActiveSection));
        // Also close any open flyout once a leaf is selected.
        if (Groups is not null)
            foreach (var g in Groups)
                g.IsFlyoutOpen = false;
        SyncActiveLeaf();
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
        GoBackCommand.NotifyCanExecuteChanged();
        GoForwardCommand.NotifyCanExecuteChanged();
    }
}
