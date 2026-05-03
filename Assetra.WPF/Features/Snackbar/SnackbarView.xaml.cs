using System.Windows;
using System.Windows.Controls;

namespace Assetra.WPF.Features.Snackbar;

public partial class SnackbarView : UserControl
{
    public SnackbarView()
    {
        InitializeComponent();
    }

    private void SnackbarItem_Loaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: SnackbarItemViewModel item })
            return;

        if (DataContext is SnackbarViewModel vm)
            vm.StartAutoDismiss(item);
    }
}
