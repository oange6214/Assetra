using System.Windows;
using System.Windows.Controls;

namespace Assetra.WPF.Features.Reports;

public partial class ReportsView : UserControl
{
    public ReportsView()
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
        if (IsVisible && DataContext is ReportsViewModel vm && !vm.IsLoading)
            vm.LoadCommand.Execute(null);
    }
}
