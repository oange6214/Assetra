using System.ComponentModel;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Wpf.Ui.Controls;

namespace Assetra.WPF.Shell;

public partial class MainWindow : FluentWindow
{
    private readonly MainViewModel _viewModel;
    private bool _suppressNavSync;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        _viewModel = viewModel;

        // Popup.CustomPopupPlacementCallback isn't XAML-settable; wire it here.
        SearchPopup.CustomPopupPlacementCallback = PlaceSearchPopup;

        _viewModel.NavRail.PropertyChanged += OnNavRailPropertyChanged;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e) =>
        SyncNavViewSelection(_viewModel.NavRail.SelectedRailSection);

    private void OnNavRailPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(NavRailViewModel.SelectedRailSection) || _suppressNavSync) return;
        Dispatcher.Invoke(() => SyncNavViewSelection(_viewModel.NavRail.SelectedRailSection));
    }

    private void SyncNavViewSelection(NavSection section)
    {
        _suppressNavSync = true;
        RootNavView.Navigate(section.ToString());
        _suppressNavSync = false;
    }

    private void RootNavView_SelectionChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressNavSync) return;
        if (RootNavView.SelectedItem is NavigationViewItem { TargetPageTag: { } tag } &&
            Enum.TryParse<NavSection>(tag, out var section))
        {
            _suppressNavSync = true;
            _viewModel.NavRail.NavigateTo(section);
            _suppressNavSync = false;
        }
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
