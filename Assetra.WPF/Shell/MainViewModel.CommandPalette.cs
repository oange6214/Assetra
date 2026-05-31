using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Assetra.WPF.Shell;

/// <summary>
/// MainViewModel partial — Command Palette (Ctrl+Shift+K) state + filter.
/// Separate from the existing global stock search (Ctrl+K) because they serve
/// different mental models: stock search is asset lookup, command palette is
/// app actions (navigate, toggle, add). Keeping them split avoids a single
/// popup with two visual sections.
/// </summary>
public partial class MainViewModel
{
    [ObservableProperty] private string _commandPaletteText = string.Empty;
    [ObservableProperty] private bool _isCommandPaletteOpen;

    private readonly ObservableCollection<CommandPaletteEntry> _commandPaletteAllEntries = new();
    private readonly ObservableCollection<CommandPaletteEntry> _commandPaletteResults = new();

    /// <summary>
    /// P2.17 T07 — LRU 最近使用過的 command TitleKey。新執行的塞到 [0]、去重、
    /// 上限 5。Empty query 時 prepend 進結果，使用者開 palette 立刻看到熟用動作。
    /// In-memory only (這個 process 的 session)；跨 session 持久化要 AppSettings 擴展。
    /// </summary>
    private readonly LinkedList<string> _commandPaletteRecentTitleKeys = new();
    private const int CommandPaletteRecentLimit = 5;

    public ReadOnlyObservableCollection<CommandPaletteEntry> CommandPaletteResults { get; private set; } = null!;

    /// <summary>
    /// Grouped view of <see cref="CommandPaletteResults"/> — GroupDescriptions on GroupKey
    /// gives the popup ListBox a 「導覽 / 動作」 section header treatment via GroupStyle.
    /// </summary>
    public ICollectionView CommandPaletteResultsView { get; private set; } = null!;

    /// <summary>
    /// Localization service is needed to resolve i18n title keys at filter time so the
    /// substring match works on the rendered Chinese/English text the user sees, not
    /// on the resource key string. Assigned by <see cref="InitializeCommandPalette"/>
    /// from the main ctor.
    /// </summary>
    private Core.Interfaces.ILocalizationService? _localizationForPalette;

    [RelayCommand]
    private void ToggleCommandPalette() => IsCommandPaletteOpen = !IsCommandPaletteOpen;

    [RelayCommand]
    private void ExecuteCommandPaletteEntry(CommandPaletteEntry? entry)
    {
        if (entry is null)
            return;
        IsCommandPaletteOpen = false;
        RecordCommandPaletteRecent(entry.TitleKey);
        entry.Execute.Invoke();
    }

    private void RecordCommandPaletteRecent(string titleKey)
    {
        // 已存在 → 移到頭；不在 → 加到頭；超 limit → 砍尾。
        var existing = _commandPaletteRecentTitleKeys.Find(titleKey);
        if (existing is not null)
            _commandPaletteRecentTitleKeys.Remove(existing);
        _commandPaletteRecentTitleKeys.AddFirst(titleKey);
        while (_commandPaletteRecentTitleKeys.Count > CommandPaletteRecentLimit)
            _commandPaletteRecentTitleKeys.RemoveLast();
    }

    partial void OnCommandPaletteTextChanged(string value) => RefilterCommandPalette(value);

    partial void OnIsCommandPaletteOpenChanged(bool value)
    {
        if (!value)
            CommandPaletteText = string.Empty;
        else
            RefilterCommandPalette(CommandPaletteText);
    }

    private void RefilterCommandPalette(string query)
    {
        _commandPaletteResults.Clear();
        var q = query?.Trim() ?? string.Empty;

        // P2.17 T07 — query 空時把 recent 5 條 prepend 到結果上方。Search 模式跳過
        // recent，因為 user 已知道要找什麼、按頻率排序意義不大反而干擾比對。
        if (string.IsNullOrEmpty(q))
        {
            const string recentGroupKey = "CommandPalette.Group.Recent";
            foreach (var titleKey in _commandPaletteRecentTitleKeys)
            {
                var match = _commandPaletteAllEntries.FirstOrDefault(e => e.TitleKey == titleKey);
                if (match is null)
                    continue;
                _commandPaletteResults.Add(match with { GroupKey = recentGroupKey });
            }
        }

        foreach (var entry in _commandPaletteAllEntries)
        {
            if (string.IsNullOrEmpty(q))
            {
                _commandPaletteResults.Add(entry);
                continue;
            }
            // 比對使用者看到的當下語系字串，不比對 resource key 本身。
            var resolved = _localizationForPalette?.Get(entry.TitleKey, entry.TitleKey) ?? entry.TitleKey;
            if (resolved.Contains(q, System.StringComparison.OrdinalIgnoreCase))
                _commandPaletteResults.Add(entry);
        }
    }

    /// <summary>
    /// Seed built-in command palette entries. Called from the main constructor right
    /// after sub-VMs are wired. Lambdas capture <c>this</c> so navigation / actions
    /// flow through the existing commands rather than duplicating logic.
    /// </summary>
    private void InitializeCommandPalette(Core.Interfaces.ILocalizationService localization)
    {
        _localizationForPalette = localization;
        void Nav(NavSection section) => NavRail.ActiveSection = section;

        var group_Nav = "Nav.Section.Title";
        var group_Action = "CommandPalette.Group.Action";

        _commandPaletteAllEntries.Add(new("FinancialOverview.Nav.Label", group_Nav, () => Nav(NavSection.FinancialOverview)));
        _commandPaletteAllEntries.Add(new("Nav.Portfolio", group_Nav, () => Nav(NavSection.Portfolio)));
        _commandPaletteAllEntries.Add(new("Nav.TransactionLog", group_Nav, () => Nav(NavSection.TransactionLog)));
        _commandPaletteAllEntries.Add(new("Nav.Reports", group_Nav, () => Nav(NavSection.Reports)));
        _commandPaletteAllEntries.Add(new("Nav.Categories", group_Nav, () => Nav(NavSection.Categories)));
        _commandPaletteAllEntries.Add(new("Nav.Assistant", group_Nav, () => Nav(NavSection.Assistant)));
        _commandPaletteAllEntries.Add(new("Nav.CashAccounts", group_Nav, () => Nav(NavSection.CashAccounts)));
        _commandPaletteAllEntries.Add(new("Nav.Liabilities", group_Nav, () => Nav(NavSection.Liabilities)));
        _commandPaletteAllEntries.Add(new("Nav.Goals", group_Nav, () => Nav(NavSection.Goals)));
        _commandPaletteAllEntries.Add(new("Fire.Title", group_Nav, () => Nav(NavSection.Fire)));
        _commandPaletteAllEntries.Add(new("MonteCarlo.Title", group_Nav, () => Nav(NavSection.MonteCarlo)));
        _commandPaletteAllEntries.Add(new("RealEstate.Title", group_Nav, () => Nav(NavSection.RealEstate)));
        _commandPaletteAllEntries.Add(new("Insurance.Title", group_Nav, () => Nav(NavSection.Insurance)));
        _commandPaletteAllEntries.Add(new("Retirement.Title", group_Nav, () => Nav(NavSection.Retirement)));
        _commandPaletteAllEntries.Add(new("PhysicalAsset.Title", group_Nav, () => Nav(NavSection.PhysicalAsset)));
        _commandPaletteAllEntries.Add(new("Nav.Recurring", group_Nav, () => Nav(NavSection.Recurring)));
        _commandPaletteAllEntries.Add(new("Nav.Alerts", group_Nav, () => Nav(NavSection.Alerts)));
        _commandPaletteAllEntries.Add(new("Nav.AuditLog", group_Nav, () => Nav(NavSection.AuditLog)));
        _commandPaletteAllEntries.Add(new("Nav.Import", group_Nav, () => Nav(NavSection.Import)));
        _commandPaletteAllEntries.Add(new("Nav.Settings", group_Nav, () => Nav(NavSection.Settings)));

        _commandPaletteAllEntries.Add(new("CommandPalette.Action.AddTransaction", group_Action, () => AddTransactionFromMenuCommand.Execute(null)));
        _commandPaletteAllEntries.Add(new("CommandPalette.Action.AddAccount", group_Action, () => AddAccountFromMenuCommand.Execute(null)));
        _commandPaletteAllEntries.Add(new("CommandPalette.Action.AddLiability", group_Action, () => AddLiabilityFromMenuCommand.Execute(null)));
        _commandPaletteAllEntries.Add(new("CommandPalette.Action.AddCategory", group_Action, () => AddCategoryFromMenuCommand.Execute(null)));
        _commandPaletteAllEntries.Add(new("CommandPalette.Action.AddGoal", group_Action, () => AddGoalFromMenuCommand.Execute(null)));
        _commandPaletteAllEntries.Add(new("CommandPalette.Action.ToggleTheme", group_Action, () => ToggleThemeCommand.Execute(null)));

        CommandPaletteResults = new ReadOnlyObservableCollection<CommandPaletteEntry>(_commandPaletteResults);
        CommandPaletteResultsView = CollectionViewSource.GetDefaultView(CommandPaletteResults);
        CommandPaletteResultsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(CommandPaletteEntry.GroupKey)));
        RefilterCommandPalette(string.Empty);
    }
}
