using System.Windows.Controls;

namespace Assetra.WPF.Features.Allocation;

public partial class FinancialOverviewView : UserControl
{
    public FinancialOverviewView()
    {
        InitializeComponent();
        IsVisibleChanged += OnIsVisibleChanged;
    }

    private void OnIsVisibleChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true && DataContext is FinancialOverviewViewModel vm)
            _ = vm.LoadAsync();
    }
}
