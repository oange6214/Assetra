using System.Windows;
using System.Windows.Controls;

namespace Assetra.WPF.Features.PortfolioGroups;

public partial class PortfolioGroupsView : UserControl
{
    public PortfolioGroupsView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        IsVisibleChanged += OnVisibilityChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => RequestLoadIfReady();

    private void OnVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true) RequestLoadIfReady();
    }

    private void RequestLoadIfReady()
    {
        if (IsVisible && DataContext is PortfolioGroupsViewModel vm && !vm.IsLoaded && !vm.IsLoading)
            vm.LoadCommand.Execute(null);
    }
}
