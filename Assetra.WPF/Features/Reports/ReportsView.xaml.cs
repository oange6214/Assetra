using System.Windows;
using System.Windows.Controls;

namespace Assetra.WPF.Features.Reports;

public partial class ReportsView : UserControl
{
    public ReportsView()
    {
        InitializeComponent();
        IsVisibleChanged += OnIsVisibleChanged;
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true && DataContext is ReportsViewModel vm && !vm.IsLoading)
            vm.LoadCommand.Execute(null);
    }
}
