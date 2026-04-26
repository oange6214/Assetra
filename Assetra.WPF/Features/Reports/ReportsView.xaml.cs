using System.Windows;
using System.Windows.Controls;

namespace Assetra.WPF.Features.Reports;

public partial class ReportsView : UserControl
{
    public ReportsView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is ReportsViewModel vm && !vm.HasReport && !vm.IsLoading)
            vm.LoadCommand.Execute(null);
    }
}
