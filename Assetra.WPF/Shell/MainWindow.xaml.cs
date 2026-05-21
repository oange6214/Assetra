using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;

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
        CommandPalettePopup.CustomPopupPlacementCallback = PlaceSearchPopup;

        // WindowStyle=None + WindowChrome makes the maximized window cover
        // the entire monitor including the Windows taskbar. Hook
        // WM_GETMINMAXINFO so the maximized size is clamped to the
        // monitor's work area (taskbar excluded).
        SourceInitialized += MainWindow_SourceInitialized;
        Loaded += MainWindow_Loaded;
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e) =>
        ClampInitialBoundsToWorkArea();

    private void ClampInitialBoundsToWorkArea()
    {
        if (WindowState != WindowState.Normal)
            return;

        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
            return;

        var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        if (monitor == IntPtr.Zero)
            return;

        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref info))
            return;

        var source = PresentationSource.FromVisual(this);
        var transform = source?.CompositionTarget?.TransformFromDevice ?? System.Windows.Media.Matrix.Identity;
        var workTopLeft = transform.Transform(new Point(info.rcWork.Left, info.rcWork.Top));
        var workBottomRight = transform.Transform(new Point(info.rcWork.Right, info.rcWork.Bottom));
        var workWidth = Math.Max(0, workBottomRight.X - workTopLeft.X);
        var workHeight = Math.Max(0, workBottomRight.Y - workTopLeft.Y);

        if (workWidth <= 0 || workHeight <= 0)
            return;

        MinWidth = Math.Min(MinWidth, workWidth);
        MinHeight = Math.Min(MinHeight, workHeight);

        var currentWidth = double.IsNaN(Width) || Width <= 0 ? ActualWidth : Width;
        var currentHeight = double.IsNaN(Height) || Height <= 0 ? ActualHeight : Height;

        Width = Math.Min(currentWidth, workWidth);
        Height = Math.Min(currentHeight, workHeight);
        Left = workTopLeft.X + Math.Max(0, (workWidth - Width) / 2);
        Top = workTopLeft.Y + Math.Max(0, (workHeight - Height) / 2);
    }

    private void SearchBackdrop_MouseDown(object sender, MouseButtonEventArgs e) =>
        _viewModel.ToggleSearchCommand.Execute(null);

    // P2.12 — Command palette backdrop dismiss (parallel to SearchBackdrop_MouseDown).
    private void CommandPaletteBackdrop_MouseDown(object sender, MouseButtonEventArgs e) =>
        _viewModel.ToggleCommandPaletteCommand.Execute(null);

    // P2.17 T04 — Shortcuts help backdrop dismiss.
    private void ShortcutsBackdrop_MouseDown(object sender, MouseButtonEventArgs e) =>
        _viewModel.ToggleShortcutsHelpCommand.Execute(null);

    // P2.13 — Auto-focus + pre-select first item when palette opens. Popup.Opened fires
    // AFTER the popup tree has IsOpen=true so Focus() will land on the rendered TextBox.
    private void CommandPalettePopup_Opened(object? sender, EventArgs e)
    {
        CommandPaletteInput.Focus();
        Keyboard.Focus(CommandPaletteInput);
        if (CommandPaletteResultsList.Items.Count > 0)
            CommandPaletteResultsList.SelectedIndex = 0;
    }

    // P2.13 — Up/Down/Enter/Esc on the input text box. Forwarded to the result list
    // for arrow keys (no SelectionChanged ripple needed since SelectedIndex setter
    // is direct) + Esc collapses + Enter executes selection-or-first.
    private void CommandPaletteInput_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var list = CommandPaletteResultsList;
        switch (e.Key)
        {
            case Key.Down:
                if (list.Items.Count > 0)
                {
                    list.SelectedIndex = Math.Min(list.SelectedIndex + 1, list.Items.Count - 1);
                    if (list.SelectedItem is not null) list.ScrollIntoView(list.SelectedItem);
                }
                e.Handled = true;
                break;
            case Key.Up:
                if (list.Items.Count > 0)
                {
                    list.SelectedIndex = list.SelectedIndex <= 0 ? 0 : list.SelectedIndex - 1;
                    if (list.SelectedItem is not null) list.ScrollIntoView(list.SelectedItem);
                }
                e.Handled = true;
                break;
            case Key.Enter:
                {
                    var pick = list.SelectedItem ?? (list.Items.Count > 0 ? list.Items[0] : null);
                    if (pick is CommandPaletteEntry entry)
                        _viewModel.ExecuteCommandPaletteEntryCommand.Execute(entry);
                    e.Handled = true;
                    break;
                }
            case Key.Escape:
                _viewModel.ToggleCommandPaletteCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

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

    // ── Maximized-window work-area clamping (WM_GETMINMAXINFO) ────────────────
    // Without this, WindowStyle=None windows render past the taskbar.

    private const int WM_GETMINMAXINFO = 0x0024;
    private const int MONITOR_DEFAULTTONEAREST = 0x00000002;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public int dwFlags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int dwFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_GETMINMAXINFO)
            return IntPtr.Zero;

        var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        if (monitor == IntPtr.Zero)
            return IntPtr.Zero;

        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref info))
            return IntPtr.Zero;

        var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
        var work = info.rcWork;
        var monArea = info.rcMonitor;

        // Position is relative to the monitor's top-left.
        mmi.ptMaxPosition.X = Math.Abs(work.Left - monArea.Left);
        mmi.ptMaxPosition.Y = Math.Abs(work.Top - monArea.Top);
        mmi.ptMaxSize.X = Math.Abs(work.Right - work.Left);
        mmi.ptMaxSize.Y = Math.Abs(work.Bottom - work.Top);
        mmi.ptMaxTrackSize.X = mmi.ptMaxSize.X;
        mmi.ptMaxTrackSize.Y = mmi.ptMaxSize.Y;

        Marshal.StructureToPtr(mmi, lParam, true);
        handled = true;
        return IntPtr.Zero;
    }

    /// <summary>
    /// 點 title-bar「+ 新增」按鈕時打開其 ContextMenu。Button.Click 觸發，
    /// 把 ContextMenu 定位到 button 下方並開啟。
    /// </summary>
    private void AddMenuButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } menu)
        {
            menu.DataContext = btn.DataContext;
            menu.PlacementTarget = btn;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }
    }
}
