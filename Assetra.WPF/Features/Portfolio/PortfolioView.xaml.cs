using System.Windows.Controls;
using System.Windows.Input;

namespace Assetra.WPF.Features.Portfolio;

public partial class PortfolioView : UserControl
{
    public PortfolioView() => InitializeComponent();

    /// <summary>
    /// Closes the cash detail overlay when the user clicks the dark backdrop.
    /// Clicks on the floating panel itself are ignored (OriginalSource != sender).
    /// </summary>
    private void OnCashDetailBackdropClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not PortfolioViewModel vm)
            return;
        if (!ReferenceEquals(e.OriginalSource, sender))
            return;
        vm.CloseCashDetailCommand.Execute(null);
        e.Handled = true;
    }

    /// <summary>
    /// Closes the liability detail overlay when the user clicks the dark backdrop.
    /// </summary>
    private void OnLiabilityDetailBackdropClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not PortfolioViewModel vm)
            return;
        if (!ReferenceEquals(e.OriginalSource, sender))
            return;
        vm.CloseLiabilityDetailCommand.Execute(null);
        e.Handled = true;
    }

    /// <summary>
    /// Closes the position detail overlay when the user clicks the dark backdrop.
    /// </summary>
    private void OnPositionDetailBackdropClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not PortfolioViewModel vm)
            return;
        if (!ReferenceEquals(e.OriginalSource, sender))
            return;
        vm.ClosePositionDetailCommand.Execute(null);
        e.Handled = true;
    }
}
