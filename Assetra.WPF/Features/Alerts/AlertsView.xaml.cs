using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Assetra.WPF.Infrastructure;

namespace Assetra.WPF.Features.Alerts;

public partial class AlertsView : UserControl
{
    public AlertsView() => InitializeComponent();

    // Cancel any editing row when clicking a different row or outside the grid
    private void OnGridPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not AlertsViewModel vm)
            return;
        var clickedRow = WpfUtils.FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject);
        foreach (var rule in vm.Rules.Where(r => r.IsEditing).ToList())
        {
            if (clickedRow?.DataContext != rule)
                rule.CancelEditCommand.Execute(null);
        }
    }

    // Cancel editing on Escape key
    private void OnGridPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;
        if (DataContext is not AlertsViewModel vm)
            return;
        foreach (var rule in vm.Rules.Where(r => r.IsEditing).ToList())
            rule.CancelEditCommand.Execute(null);
        e.Handled = true;
    }
}
