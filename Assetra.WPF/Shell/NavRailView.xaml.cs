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

    private void NavItem_Click(object sender, RoutedEventArgs e)
    {
        if (_navRail is null) return;

        if (sender is ToggleButton { Tag: NavSection section } button)
        {
            _navRail.NavigateTo(section);
            button.SetCurrentValue(ToggleButton.IsCheckedProperty, true);
        }
    }
}
