using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Assetra.WPF.Shell;

public partial class NavRailView : UserControl
{
    private NavRailViewModel? _navRail;

    public NavRailView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        _navRail = (e.NewValue as MainViewModel)?.NavRail;
        if (_navRail is null) return;
        Dispatcher.BeginInvoke(() => UpdateLayout());
    }

    /// <summary>
    /// Click handler for any leaf row (expanded list, popup, or bottom items).
    /// The Tag carries the <see cref="NavLeafVm"/>; we navigate to its section
    /// and force IsChecked back to its bound source so a quick double-click on
    /// the active leaf doesn't accidentally desync the toggle visual.
    /// </summary>
    private void NavLeaf_Click(object sender, RoutedEventArgs e)
    {
        if (_navRail is null) return;
        if (sender is not ToggleButton { Tag: NavLeafVm leaf } button) return;

        _navRail.NavigateTo(leaf.Section);
        button.SetCurrentValue(ToggleButton.IsCheckedProperty, leaf.IsActive);
    }

    /// <summary>
    /// Click handler for collapsed-mode group icon — opens the flyout Popup
    /// containing the group's children. The IsChecked source is the
    /// HasActiveChild flag so the visual stays bound to "this group owns the
    /// current section"; we toggle the IsFlyoutOpen flag explicitly.
    /// </summary>
    private void NavGroupIcon_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton { Tag: NavGroupVm group } button) return;

        group.IsFlyoutOpen = !group.IsFlyoutOpen;
        // Reset the IsChecked visual back to whatever HasActiveChild is —
        // the user clicking the icon shouldn't permanently "select" the group.
        button.SetCurrentValue(ToggleButton.IsCheckedProperty, group.HasActiveChild);
    }
}
