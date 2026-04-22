using Wpf.Ui.Controls;
using System.Windows.Input;

namespace Assetra.WPF.Features.AddStock;

public partial class AddStockDialog : FluentWindow
{
    public AddStockDialog(AddStockViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.CloseRequested += () => Close();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape)
                return;
            if (viewModel.CancelCommand.CanExecute(null))
                viewModel.CancelCommand.Execute(null);
            e.Handled = true;
        };
    }
}
