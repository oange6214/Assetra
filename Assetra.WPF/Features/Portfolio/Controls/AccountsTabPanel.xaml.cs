using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Assetra.WPF.Infrastructure;

namespace Assetra.WPF.Features.Portfolio.Controls;

public partial class AccountsTabPanel : UserControl
{
    public AccountsTabPanel() => InitializeComponent();

    private void OnGridPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        var src = e.OriginalSource as DependencyObject;

        if (WpfUtils.IsInsideActionControl(src))
            return;

        var clickedRow = WpfUtils.FindAncestor<DataGridRow>(src);
        if (e.ChangedButton == MouseButton.Left && clickedRow is not null && !clickedRow.IsSelected)
            clickedRow.IsSelected = true;
    }
}
