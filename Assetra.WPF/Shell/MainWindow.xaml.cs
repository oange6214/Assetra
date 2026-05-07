using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace Assetra.WPF.Shell;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        _viewModel = viewModel;

        // Popup.CustomPopupPlacementCallback isn't XAML-settable; wire it here.
        SearchPopup.CustomPopupPlacementCallback = PlaceSearchPopup;
    }

    private void SearchBackdrop_MouseDown(object sender, MouseButtonEventArgs e) =>
        _viewModel.ToggleSearchCommand.Execute(null);

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void MaximizeRestoreButton_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    private void CloseButton_Click(object sender, RoutedEventArgs e) =>
        Close();

    // Horizontally centers the search popup near the top of the window, matching
    // the previous command-palette placement.
    private static CustomPopupPlacement[] PlaceSearchPopup(Size popupSize, Size targetSize, Point offset)
    {
        var x = (targetSize.Width - popupSize.Width) / 2;
        const double topInset = 48; // leaves the title bar visible above the card
        return [new CustomPopupPlacement(new Point(x, topInset), PopupPrimaryAxis.Horizontal)];
    }
}
