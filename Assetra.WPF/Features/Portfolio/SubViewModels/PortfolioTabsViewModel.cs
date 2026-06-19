using System.Collections.ObjectModel;
using Assetra.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Assetra.WPF.Features.Portfolio.SubViewModels;

public sealed partial class PortfolioTabViewModel : ObservableObject
{
    public bool IsAll { get; init; }
    public Guid? GroupId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? ColorHex { get; init; }
}

public sealed partial class PortfolioTabsViewModel : ObservableObject
{
    public ObservableCollection<PortfolioTabViewModel> Tabs { get; } = [];

    [ObservableProperty]
    private PortfolioTabViewModel? _selectedTab;

    public Guid? SelectedGroupId => SelectedTab?.GroupId;

    public PortfolioTabsViewModel(IEnumerable<PortfolioGroup> groups, string allLabel, string ungroupedLabel)
        => Rebuild(groups, allLabel, ungroupedLabel, showUngrouped: false, keepSelection: null);

    partial void OnSelectedTabChanged(PortfolioTabViewModel? value) =>
        OnPropertyChanged(nameof(SelectedGroupId));

    /// <summary>
    /// Rebuilds from the latest catalog, preserving the selection by GroupId.
    /// <paramref name="showUngrouped"/> controls whether the「未指定組合」tab is offered — it
    /// should appear only when ungrouped positions actually exist, otherwise it is an empty
    /// bucket cluttering the strip.
    /// </summary>
    public void Sync(IEnumerable<PortfolioGroup> groups, string allLabel, string ungroupedLabel, bool showUngrouped)
        => Rebuild(groups, allLabel, ungroupedLabel, showUngrouped, keepSelection: SelectedGroupId);

    private void Rebuild(
        IEnumerable<PortfolioGroup> groups, string allLabel, string ungroupedLabel, bool showUngrouped, Guid? keepSelection)
    {
        var users = groups.Where(g => !g.IsSystem).ToList();
        Tabs.Clear();
        Tabs.Add(new PortfolioTabViewModel { IsAll = true, Name = allLabel });
        if (users.Count > 0)
        {
            if (showUngrouped)
                Tabs.Add(new PortfolioTabViewModel { GroupId = PortfolioGroup.DefaultId, Name = ungroupedLabel });
            foreach (var g in users)
                Tabs.Add(new PortfolioTabViewModel { GroupId = g.Id, Name = g.Name, ColorHex = g.ColorHex });
        }
        SelectedTab = Tabs.FirstOrDefault(t => t.GroupId == keepSelection) ?? Tabs[0];
    }
}
