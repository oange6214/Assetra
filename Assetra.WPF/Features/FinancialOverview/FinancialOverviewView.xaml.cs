using System.Windows.Controls;
using System.Windows.Input;
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

    /// <summary>
    /// Backdrop click closes the KPI editor without saving — same affordance
    /// as Escape key (wired via EscapeKeyCommandBehavior on the overlay grid).
    /// </summary>
    private void KpiEditorBackdrop_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is FinancialOverviewViewModel vm && vm.CloseKpiEditorCommand.CanExecute(null))
            vm.CloseKpiEditorCommand.Execute(null);
    }
}
