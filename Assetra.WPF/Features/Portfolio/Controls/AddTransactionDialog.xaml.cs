using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Assetra.WPF.Features.Portfolio.SubViewModels;

namespace Assetra.WPF.Features.Portfolio.Controls;

public partial class AddRecordDialog : UserControl
{
    public AddRecordDialog()
    {
        InitializeComponent();
        IsVisibleChanged += OnIsVisibleChanged;
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if ((bool)e.NewValue)
            ContentScrollViewer.ScrollToTop();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;
        if (DataContext is not TransactionDialogViewModel vm)
            return;
        if (!vm.IsTxDialogOpen)
            return;

        if (vm.CloseTxDialogCommand.CanExecute(null))
            vm.CloseTxDialogCommand.Execute(null);
        e.Handled = true;
    }
}
