using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Assetra.WPF.Features.AddStock;
using Wpf.Ui.Controls;

namespace Assetra.WPF.Shell;

public partial class MainWindow : FluentWindow
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel, IServiceProvider services)
    {
        InitializeComponent();
        DataContext = viewModel;
        _viewModel = viewModel;

        // Popup.CustomPopupPlacementCallback isn't XAML-settable; wire it here.
        SearchPopup.CustomPopupPlacementCallback = PlaceSearchPopup;

        WeakReferenceMessenger.Default.Register<OpenAddStockMessage>(this, (_, _) =>
        {
            // Reset clears previous search so each open starts with a blank slate.
            var vm = services.GetRequiredService<AddStockViewModel>();
            vm.Reset();
            var dlg = new AddStockDialog(vm) { Owner = this };
            dlg.ShowDialog();
        });
    }

    // Backdrop click handlers

    private void SearchBackdrop_MouseDown(object sender, MouseButtonEventArgs e) =>
        _viewModel.ToggleSearchCommand.Execute(null);

    // Horizontally centers the search popup near the top of the window, matching
    // the previous command-palette placement.
    private static CustomPopupPlacement[] PlaceSearchPopup(Size popupSize, Size targetSize, Point offset)
    {
        var x = (targetSize.Width - popupSize.Width) / 2;
        const double topInset = 48; // leaves the title bar visible above the card
        return [new CustomPopupPlacement(new Point(x, topInset), PopupPrimaryAxis.Horizontal)];
    }
}
