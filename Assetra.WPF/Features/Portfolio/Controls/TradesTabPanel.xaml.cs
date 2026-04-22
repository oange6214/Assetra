using System.Windows.Controls;
using System.Windows.Input;

namespace Assetra.WPF.Features.Portfolio.Controls;

public partial class TradesTabPanel : UserControl
{
    public TradesTabPanel() => InitializeComponent();

    internal static bool TryOpenTradeEditor(PortfolioViewModel? vm, object? item)
    {
        if (vm is null || item is null)
            return false;

        if (!vm.Transaction.EditTradeCommand.CanExecute(item))
            return false;

        vm.Transaction.EditTradeCommand.Execute(item);
        return true;
    }

    // EventSetter on DataGridRow — sender IS the row, DataContext is the trade item.
    private void OnRowClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not PortfolioViewModel vm) return;
        if (sender is not DataGridRow { DataContext: { } item }) return;

        TryOpenTradeEditor(vm, item);

        e.Handled = true;
    }
}
