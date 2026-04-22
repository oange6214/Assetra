using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace Assetra.WPF.Controls;

/// <summary>
/// 自製日曆面板：純 XAML UniformGrid + Button，不使用 WPF 內建 Calendar。
/// 週一起始，今天用小圓點標示，選取日用 AppAccent 背景圓角。
/// </summary>
public partial class CalendarPanel : UserControl
{
    // Dependency Properties

    public static readonly DependencyProperty SelectedDateProperty =
        DependencyProperty.Register(
            nameof(SelectedDate),
            typeof(DateTime?),
            typeof(CalendarPanel),
            new FrameworkPropertyMetadata(
                null,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnSelectedDateChanged));

    public static readonly DependencyProperty DisplayDateEndProperty =
        DependencyProperty.Register(
            nameof(DisplayDateEnd),
            typeof(DateTime?),
            typeof(CalendarPanel),
            new PropertyMetadata(null, (d, _) => ((CalendarPanel)d).Rebuild()));

    public DateTime? SelectedDate
    {
        get => (DateTime?)GetValue(SelectedDateProperty);
        set => SetValue(SelectedDateProperty, value);
    }

    public DateTime? DisplayDateEnd
    {
        get => (DateTime?)GetValue(DisplayDateEndProperty);
        set => SetValue(DisplayDateEndProperty, value);
    }

    // Events

    public event EventHandler<DateTime>? DatePicked;

    // State

    private int _viewYear;
    private int _viewMonth;
    private bool _inYearView;
    private int _yearPageStart;

    private static readonly string[] DayHeaders = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];

    // Constructor

    public CalendarPanel()
    {
        InitializeComponent();

        var now = DateTime.Today;
        _viewYear = now.Year;
        _viewMonth = now.Month;

        Loaded += (_, _) => Rebuild();
    }

    // Navigation

    private void Header_Click(object sender, RoutedEventArgs e)
    {
        _yearPageStart = _viewYear - 5;
        _inYearView = true;
        RebuildYearView();
    }

    private void PrevMonth_Click(object sender, RoutedEventArgs e)
    {
        if (_inYearView)
        {
            _yearPageStart -= 12;
            RebuildYearView();
            return;
        }
        _viewMonth--;
        if (_viewMonth < 1) { _viewMonth = 12; _viewYear--; }
        Rebuild();
    }

    private void NextMonth_Click(object sender, RoutedEventArgs e)
    {
        if (_inYearView)
        {
            _yearPageStart += 12;
            RebuildYearView();
            return;
        }
        _viewMonth++;
        if (_viewMonth > 12) { _viewMonth = 1; _viewYear++; }
        Rebuild();
    }

    private void Today_Click(object sender, RoutedEventArgs e)
    {
        var today = DateTime.Today;
        _viewYear = today.Year;
        _viewMonth = today.Month;
        SelectedDate = today;
        DatePicked?.Invoke(this, today);
    }

    // Core: build the 7×7 grid

    private static void OnSelectedDateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var panel = (CalendarPanel)d;
        if (e.NewValue is DateTime dt)
        {
            panel._viewYear = dt.Year;
            panel._viewMonth = dt.Month;
        }
        panel.Rebuild();
    }

    private void RebuildYearView()
    {
        DayGrid.Visibility = Visibility.Collapsed;
        YearGrid.Visibility = Visibility.Visible;

        HeaderText.Text = $"{_yearPageStart} – {_yearPageStart + 11}";

        YearGrid.Children.Clear();

        var maxYear = DisplayDateEnd?.Year;

        for (var i = 0; i < 12; i++)
        {
            var year = _yearPageStart + i;
            var isSelected = year == _viewYear;
            var isDisabled = maxYear.HasValue && year > maxYear.Value;

            var btn = BuildYearCell(year, isSelected, isDisabled);
            if (!isDisabled)
            {
                var captured = year;
                btn.Click += (_, _) =>
                {
                    _viewYear = captured;
                    _inYearView = false;
                    Rebuild();
                };
            }
            YearGrid.Children.Add(btn);
        }
    }

    private Button BuildYearCell(int year, bool isSelected, bool isDisabled)
    {
        var btn = new Button
        {
            Style = (Style)FindResource("DayCellBtn"),
            IsEnabled = !isDisabled,
            Height = 44,
        };

        if (isSelected)
            btn.Background = FindBrush("AppAccent");

        var text = new TextBlock
        {
            Text = year.ToString(),
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = isSelected ? Brushes.White
                : isDisabled ? FindBrush("AppTextMuted")
                : FindBrush("AppTextPrimary"),
        };

        btn.Content = text;
        return btn;
    }

    private void Rebuild()
    {
        if (!IsLoaded)
            return;

        _inYearView = false;
        DayGrid.Visibility = Visibility.Visible;
        YearGrid.Visibility = Visibility.Collapsed;
        DayGrid.Opacity = 0;
        DayGrid.Children.Clear();

        // Header text
        HeaderText.Text = new DateTime(_viewYear, _viewMonth, 1)
            .ToString("MMMM yyyy", CultureInfo.InvariantCulture);

        var today = DateTime.Today;
        var selected = SelectedDate;
        var maxDate = DisplayDateEnd;

        // Row 0: day-of-week headers
        foreach (var hdr in DayHeaders)
        {
            DayGrid.Children.Add(new TextBlock
            {
                Text = hdr,
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = FindBrush("AppTextMuted"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 4, 0, 6),
            });
        }

        // Rows 1-6: day cells
        var firstOfMonth = new DateTime(_viewYear, _viewMonth, 1);
        var daysInMonth = DateTime.DaysInMonth(_viewYear, _viewMonth);

        // Monday-based offset: Monday=0 … Sunday=6
        var startDow = ((int)firstOfMonth.DayOfWeek + 6) % 7;

        // Fill 6 weeks = 42 cells
        var cellDate = firstOfMonth.AddDays(-startDow);

        for (var i = 0; i < 42; i++)
        {
            var dt = cellDate;
            var isCurrentMonth = dt.Month == _viewMonth && dt.Year == _viewYear;
            var isToday = dt.Date == today;
            var isSelected = selected.HasValue && dt.Date == selected.Value.Date;
            var isDisabled = maxDate.HasValue && dt.Date > maxDate.Value.Date;

            var cell = BuildDayCell(dt.Day, isCurrentMonth, isToday, isSelected, isDisabled);

            if (!isDisabled)
            {
                var captured = dt;
                cell.Click += (_, _) =>
                {
                    SelectedDate = captured;
                    DatePicked?.Invoke(this, captured);
                };
            }

            DayGrid.Children.Add(cell);
            cellDate = cellDate.AddDays(1);
        }

        // Fade in the rebuilt grid
        DayGrid.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(180)))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            });
    }

    // Build a single day cell

    private Button BuildDayCell(int day, bool isCurrentMonth, bool isToday, bool isSelected, bool isDisabled)
    {
        var btn = new Button
        {
            Style = (Style)FindResource("DayCellBtn"),
            IsEnabled = !isDisabled,
            Height = 36,
        };

        if (isSelected)
            btn.Background = FindBrush("AppAccent");

        // Content: StackPanel with day number + optional today dot
        var stack = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var dayText = new TextBlock
        {
            Text = day.ToString(),
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
            FontWeight = isToday ? FontWeights.Bold : FontWeights.Normal,
        };

        if (isSelected)
            dayText.Foreground = Brushes.White;
        else if (!isCurrentMonth || isDisabled)
            dayText.Foreground = FindBrush("AppTextMuted");
        else if (isToday)
            dayText.Foreground = FindBrush("AppAccent");
        else
            dayText.Foreground = FindBrush("AppTextPrimary");

        stack.Children.Add(dayText);

        // Today dot (small circle below number)
        if (isToday && !isSelected)
        {
            stack.Children.Add(new Ellipse
            {
                Width = 4,
                Height = 4,
                Fill = FindBrush("AppAccent"),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 1, 0, 0),
            });
        }

        btn.Content = stack;
        return btn;
    }

    // Helpers

    private Brush FindBrush(string key) =>
        TryFindResource(key) as Brush ?? Brushes.Gray;
}
