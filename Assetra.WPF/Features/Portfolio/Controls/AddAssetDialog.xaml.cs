using System.Windows.Controls;
using System.Windows.Input;

namespace Assetra.WPF.Features.Portfolio.Controls;

public partial class AddAssetDialog : UserControl
{
    public AddAssetDialog()
    {
        InitializeComponent();
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;
        if (DataContext is not PortfolioViewModel vm)
            return;
        if (!vm.AddAssetDialog.IsAddDialogOpen)
            return;

        if (vm.AddAssetDialog.CloseAddDialogCommand.CanExecute(null))
            vm.AddAssetDialog.CloseAddDialogCommand.Execute(null);
        e.Handled = true;
    }
}
