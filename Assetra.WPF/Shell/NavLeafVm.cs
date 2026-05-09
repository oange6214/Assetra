using CommunityToolkit.Mvvm.ComponentModel;

namespace Assetra.WPF.Shell;

/// <summary>
/// Leaf navigation entry — a single section selectable from the rail.
/// Wired up data-driven (vs. previously hard-coded ToggleButton per section).
/// Ownership: created by <see cref="NavRailViewModel"/>; the View binds to
/// <see cref="Section"/>, <see cref="LocalizedLabel"/>, <see cref="IconSymbol"/>,
/// <see cref="ToolTip"/>, and <see cref="IsActive"/>.
/// </summary>
public sealed partial class NavLeafVm : ObservableObject
{
    public required NavSection Section { get; init; }
    public required string LabelResourceKey { get; init; }
    public required string IconSymbol { get; init; }
    public required string ToolTipResourceKey { get; init; }

    /// <summary>
    /// Optional tag identifying this leaf as a badge-bearing entry. Used by the
    /// View's DataTrigger to choose which badge content (recurring count vs.
    /// alert count) to render. Null = no badge.
    /// </summary>
    public string? BadgeKind { get; init; }

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private string _localizedLabel = string.Empty;

    [ObservableProperty]
    private string _localizedToolTip = string.Empty;
}
