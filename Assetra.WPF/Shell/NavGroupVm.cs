using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Assetra.WPF.Shell;

/// <summary>
/// Group of nav leaves shown together (Overview / Assets / Cashflow / Planning).
/// In expanded rail mode the group renders as a collapsible header + child list.
/// In collapsed rail mode the group renders as a single icon button that opens
/// a Popup flyout containing the children — matches WinUI 3 NavigationView's
/// Compact pane mode.
/// </summary>
public sealed partial class NavGroupVm : ObservableObject
{
    public required string TitleResourceKey { get; init; }
    public required string GroupIconSymbol { get; init; }
    public required IReadOnlyList<NavLeafVm> Items { get; init; }

    [ObservableProperty]
    private bool _isExpanded = true;

    /// <summary>
    /// True when one of this group's leaves is the current ActiveSection.
    /// Used in the View to subtly highlight the group icon in collapsed mode
    /// and to keep the group auto-expanded after section changes.
    /// </summary>
    [ObservableProperty]
    private bool _hasActiveChild;

    /// <summary>True while the collapsed-mode flyout for this group is open.</summary>
    [ObservableProperty]
    private bool _isFlyoutOpen;

    [ObservableProperty]
    private string _localizedTitle = string.Empty;

    [RelayCommand]
    private void ToggleExpanded() => IsExpanded = !IsExpanded;

    [RelayCommand]
    private void OpenFlyout() => IsFlyoutOpen = true;

    [RelayCommand]
    private void CloseFlyout() => IsFlyoutOpen = false;
}
