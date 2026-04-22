using System.Windows.Controls;
using System.Windows.Input;

namespace Assetra.WPF.Features.Allocation.Controls;

public partial class EditTargetsOverlay : UserControl
{
    public EditTargetsOverlay()
    {
        InitializeComponent();
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;
        if (DataContext is not AllocationViewModel vm)
            return;
        if (!vm.IsEditingTargets)
            return;

        if (vm.CancelEditTargetsCommand.CanExecute(null))
            vm.CancelEditTargetsCommand.Execute(null);
        e.Handled = true;
    }
}
