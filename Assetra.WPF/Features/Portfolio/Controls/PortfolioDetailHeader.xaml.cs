using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Assetra.WPF.Features.Portfolio.Controls;

/// <summary>
/// Code-behind for PortfolioDetailHeader.xaml — state is on PortfolioViewModel via
/// data binding; the only logic is opening the ＋新增交易 split-button dropdown on click.
/// </summary>
public partial class PortfolioDetailHeader : UserControl
{
    public PortfolioDetailHeader() => InitializeComponent();

    /// <summary>
    /// Opens the split-button's secondary menu (加入觀察) on left-click of the ▾ button —
    /// mirrors the QuickAdd pattern in PortfolioView so a Button can act as a dropdown.
    /// </summary>
    private void OnAddMenuClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.ContextMenu is null)
            return;
        btn.ContextMenu.PlacementTarget = btn;
        btn.ContextMenu.Placement = PlacementMode.Bottom;
        btn.ContextMenu.IsOpen = true;
    }
}
