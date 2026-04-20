using System.Windows.Controls;
using System.Windows.Input;

namespace Assetra.WPF.Features.Portfolio.Controls;

public partial class TradesTabPanel : UserControl
{
    public TradesTabPanel() => InitializeComponent();

    // EventSetter on DataGridRow — sender IS the row, DataContext is the trade item.
    private void OnRowClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not PortfolioViewModel vm) return;
        if (sender is not DataGridRow { DataContext: { } item }) return;

        if (vm.EditTradeCommand.CanExecute(item))
            vm.EditTradeCommand.Execute(item);

        e.Handled = true;
    }
}
