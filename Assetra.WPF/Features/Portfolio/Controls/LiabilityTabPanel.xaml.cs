using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Assetra.WPF.Infrastructure;

namespace Assetra.WPF.Features.Portfolio.Controls;

public partial class LiabilityTabPanel : UserControl
{
    public LiabilityTabPanel() => InitializeComponent();

    private void OnGridPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        var clickedRow = WpfUtils.FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject);
        if (e.ChangedButton == MouseButton.Left && clickedRow is not null && !clickedRow.IsSelected)
            clickedRow.IsSelected = true;
    }
}
