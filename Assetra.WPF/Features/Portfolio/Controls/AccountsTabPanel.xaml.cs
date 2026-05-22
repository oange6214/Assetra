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

    // P4.9i — Cash detail panel CTA：點「新增紀錄」彈出 ContextMenu (income/deposit/...)。
    // 從 MainWindow shell 搬下來，跟著 overlay 一起 page-scoped。
    private void AddMenuButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.ContextMenu is { } menu)
        {
            menu.DataContext = btn.DataContext;
            menu.PlacementTarget = btn;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }
    }
}
