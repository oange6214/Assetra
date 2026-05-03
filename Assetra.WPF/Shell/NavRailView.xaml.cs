using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;

namespace Assetra.WPF.Shell;

public partial class NavRailView : UserControl
{
    private NavRailViewModel? _navRail;
    private bool _suppressNavSync;

    public NavRailView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_navRail is not null)
            _navRail.PropertyChanged -= OnNavRailPropertyChanged;

        _navRail = (e.NewValue as MainViewModel)?.NavRail;
        if (_navRail is null) return;

        _navRail.PropertyChanged += OnNavRailPropertyChanged;
        Dispatcher.BeginInvoke(() => SyncNavViewSelection(_navRail.SelectedRailSection));
    }

    private void OnNavRailPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(NavRailViewModel.SelectedRailSection) || _suppressNavSync || _navRail is null) return;
        Dispatcher.Invoke(() => SyncNavViewSelection(_navRail.SelectedRailSection));
    }

    private void SyncNavViewSelection(NavSection section)
    {
        _suppressNavSync = true;
        RootNavView.Navigate(section.ToString());
        _suppressNavSync = false;
    }

    private void RootNavView_SelectionChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressNavSync || _navRail is null) return;
        if (RootNavView.SelectedItem is NavigationViewItem { TargetPageTag: { } tag } &&
            Enum.TryParse<NavSection>(tag, out var section))
        {
            _suppressNavSync = true;
            _navRail.NavigateTo(section);
            _suppressNavSync = false;
        }
    }
}
