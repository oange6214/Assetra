using System.Windows.Controls;
using Assetra.WPF.Infrastructure;

namespace Assetra.WPF.Features.FinancialOverview;

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
            AsyncHelpers.SafeFireAndForget(vm.LoadAsync, "FinancialOverview.Load");
    }
}
