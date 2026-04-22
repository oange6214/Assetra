using System.Windows.Controls;
using System.Windows.Input;
using Assetra.WPF.Features.Portfolio.SubViewModels;

namespace Assetra.WPF.Features.Portfolio.Controls;

public partial class EditAssetDialog : UserControl
{
    public EditAssetDialog()
    {
        InitializeComponent();
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;
        if (DataContext is not AccountDialogViewModel vm)
            return;
        if (!vm.IsEditAssetDialogOpen)
            return;

        if (vm.CloseEditAssetCommand.CanExecute(null))
            vm.CloseEditAssetCommand.Execute(null);
        e.Handled = true;
    }
}
