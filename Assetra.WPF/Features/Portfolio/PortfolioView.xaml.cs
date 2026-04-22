using System.Windows.Controls;
using System.Windows.Input;

namespace Assetra.WPF.Features.Portfolio;

public partial class PortfolioView : UserControl
{
    public PortfolioView()
    {
        InitializeComponent();
        PreviewKeyDown += OnPreviewKeyDown;
    }

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

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;
        if (DataContext is not PortfolioViewModel vm)
            return;

        if (vm.IsConfirmDialogOpen)
        {
            if (vm.ConfirmDialogNoCommand.CanExecute(null))
                vm.ConfirmDialogNoCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (vm.Transaction.IsTxDialogOpen)
        {
            if (vm.Transaction.CloseTxDialogCommand.CanExecute(null))
                vm.Transaction.CloseTxDialogCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (vm.AddAssetDialog.IsAddDialogOpen)
        {
            if (vm.AddAssetDialog.CloseAddDialogCommand.CanExecute(null))
                vm.AddAssetDialog.CloseAddDialogCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (vm.Account.IsEditAssetDialogOpen)
        {
            if (vm.Account.CloseEditAssetCommand.CanExecute(null))
                vm.Account.CloseEditAssetCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (vm.SellPanel.IsSellPanelVisible)
        {
            if (vm.SellPanel.CancelSellCommand.CanExecute(null))
                vm.SellPanel.CancelSellCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (vm.HasSelectedPositionRow)
        {
            if (vm.ClosePositionDetailCommand.CanExecute(null))
                vm.ClosePositionDetailCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (vm.HasSelectedLiabilityRow)
        {
            if (vm.CloseLiabilityDetailCommand.CanExecute(null))
                vm.CloseLiabilityDetailCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (vm.HasSelectedCashRow)
        {
            if (vm.CloseCashDetailCommand.CanExecute(null))
                vm.CloseCashDetailCommand.Execute(null);
            e.Handled = true;
        }
    }
}
