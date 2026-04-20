using Wpf.Ui.Controls;

namespace Assetra.WPF.Features.AddStock;

public partial class AddStockDialog : FluentWindow
{
    public AddStockDialog(AddStockViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.CloseRequested += () => Close();
    }
}
