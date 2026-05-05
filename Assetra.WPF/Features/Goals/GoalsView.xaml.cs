using System.Windows;
using System.Windows.Controls;

namespace Assetra.WPF.Features.Goals;

public partial class GoalsView : UserControl
{
    public GoalsView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        DataContextChanged += OnDataContextChanged;
        IsVisibleChanged += OnIsVisibleChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RequestLoadIfReady();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        RequestLoadIfReady();
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
            RequestLoadIfReady();
    }

    private void RequestLoadIfReady()
    {
        if (IsVisible && DataContext is GoalsViewModel vm && !vm.IsLoaded && !vm.IsLoading)
            vm.LoadCommand.Execute(null);
    }
}
