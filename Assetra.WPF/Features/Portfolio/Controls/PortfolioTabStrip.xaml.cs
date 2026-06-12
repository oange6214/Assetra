using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Assetra.WPF.Features.Portfolio.Controls;

public partial class PortfolioTabStrip : UserControl
{
    public PortfolioTabStrip()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Redirect vertical mouse-wheel events to horizontal scrolling so that
    /// the pill row scrolls left/right even though it has no vertical content.
    /// </summary>
    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer sv)
            return;

        sv.ScrollToHorizontalOffset(sv.HorizontalOffset - e.Delta);
        e.Handled = true;
    }
}
