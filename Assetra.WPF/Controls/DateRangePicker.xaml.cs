using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;

namespace Assetra.WPF.Controls;

/// <summary>
/// 雙月行事曆範圍選擇器：
///   - 單一觸發按鈕，顯示「起 - 迄」文字 + 清除 X
///   - Popup 內含：可輸入的起迄 TextBox + 重設按鈕 + 兩個月份並排 grid
///   - 點日格：第一下設 StartDate；第二下設 EndDate（若晚於起始）；之後再點重新開始
/// </summary>
public partial class DateRangePicker : UserControl, INotifyPropertyChanged
{
    public static readonly DependencyProperty StartDateProperty =
        DependencyProperty.Register(nameof(StartDate), typeof(DateTime?), typeof(DateRangePicker),
            new FrameworkPropertyMetadata(null,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                (d, e) => ((DateRangePicker)d).OnRangeChanged()));

    public DateTime? StartDate
    {
        get => (DateTime?)GetValue(StartDateProperty);
        set => SetValue(StartDateProperty, value);
    }

    public static readonly DependencyProperty EndDateProperty =
        DependencyProperty.Register(nameof(EndDate), typeof(DateTime?), typeof(DateRangePicker),
            new FrameworkPropertyMetadata(null,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                (d, e) => ((DateRangePicker)d).OnRangeChanged()));

    public DateTime? EndDate
    {
        get => (DateTime?)GetValue(EndDateProperty);
        set => SetValue(EndDateProperty, value);
    }

    public static readonly DependencyProperty PlaceholderProperty =
        DependencyProperty.Register(nameof(Placeholder), typeof(string), typeof(DateRangePicker),
            new PropertyMetadata("選擇日期範圍"));

    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    // ── Internal state ──────────────────────────────

    private bool _isPopupOpen;
    public bool IsPopupOpen
    {
        get => _isPopupOpen;
        set
        {
            if (_isPopupOpen == value) return;
            _isPopupOpen = value;
            // 開啟時如果已有起始，跳到那個月
            if (value && StartDate is { } s)
                LeftMonth = new DateTime(s.Year, s.Month, 1);
            RaisePropertyChanged();
        }
    }

    private DateTime _leftMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    public DateTime LeftMonth
    {
        get => _leftMonth;
        set
        {
            if (_leftMonth == value) return;
            _leftMonth = value;
            RaisePropertyChanged();
            RaisePropertyChanged(nameof(RightMonth));
            RaisePropertyChanged(nameof(LeftMonthLabel));
            RaisePropertyChanged(nameof(RightMonthLabel));
            RebuildCalendar();
        }
    }

    public DateTime RightMonth => LeftMonth.AddMonths(1);

    public string LeftMonthLabel => LeftMonth.ToString("MMMM yyyy", CultureInfo.InvariantCulture);
    public string RightMonthLabel => RightMonth.ToString("MMMM yyyy", CultureInfo.InvariantCulture);

    /// <summary>目前顯示文字：空 → placeholder；有值 → "MMM dd, yyyy - MMM dd, yyyy"。</summary>
    public string DisplayText
    {
        get
        {
            if (StartDate is null && EndDate is null) return Placeholder;
            var a = StartDate?.ToString("MMM dd, yyyy", CultureInfo.InvariantCulture) ?? "…";
            var b = EndDate?.ToString("MMM dd, yyyy", CultureInfo.InvariantCulture) ?? "…";
            return $"{a} - {b}";
        }
    }

    /// <summary>有任何日期 → 顯示清除 X。</summary>
    public bool HasValue => StartDate.HasValue || EndDate.HasValue;

    /// <summary>TextBox 直接輸入用（yyyy-MM-dd 格式）。</summary>
    public string StartDateText
    {
        get => StartDate?.ToString("yyyy-MM-dd") ?? string.Empty;
        set
        {
            if (DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            {
                StartDate = d;
                if (EndDate.HasValue && EndDate.Value.Date < d.Date)
                    EndDate = null;
            }
            else if (string.IsNullOrWhiteSpace(value))
                StartDate = null;
            RaisePropertyChanged();
        }
    }

    public string EndDateText
    {
        get => EndDate?.ToString("yyyy-MM-dd") ?? string.Empty;
        set
        {
            if (DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            {
                EndDate = d;
                if (StartDate.HasValue && StartDate.Value.Date > d.Date)
                    StartDate = null;
            }
            else if (string.IsNullOrWhiteSpace(value))
                EndDate = null;
            RaisePropertyChanged();
        }
    }

    public ObservableCollection<DayCell> LeftDays { get; } = [];
    public ObservableCollection<DayCell> RightDays { get; } = [];

    // ── Ctor ──────────────────────────────

    public DateRangePicker()
    {
        InitializeComponent();
        RebuildCalendar();
    }

    // ── Event handlers ──────────────────────────────

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        StartDate = null;
        EndDate = null;
        // 避免事件冒泡到外層 ToggleButton 誤觸開關 popup
        e.Handled = true;
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        StartDate = null;
        EndDate = null;
    }

    private void PrevMonth_Click(object sender, RoutedEventArgs e) => LeftMonth = LeftMonth.AddMonths(-1);
    private void NextMonth_Click(object sender, RoutedEventArgs e) => LeftMonth = LeftMonth.AddMonths(1);

    private void DayCell_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.DataContext is not DayCell cell) return;
        var d = cell.Date;

        if (StartDate is null || EndDate is not null)
        {
            // 新一輪選取
            StartDate = d;
            EndDate = null;
        }
        else if (d < StartDate)
        {
            // 點到更早的日期 → 改設為起始（重新開始）
            StartDate = d;
            EndDate = null;
        }
        else
        {
            // 完成範圍（不自動關閉 popup — 讓使用者看到高亮範圍，
            // 確認無誤後再點 popup 外關閉）
            EndDate = d;
        }
    }

    // ── Calendar rebuild ──────────────────────────────

    private void OnRangeChanged()
    {
        RaisePropertyChanged(nameof(DisplayText));
        RaisePropertyChanged(nameof(HasValue));
        RaisePropertyChanged(nameof(StartDateText));
        RaisePropertyChanged(nameof(EndDateText));
        RebuildCalendar();
    }

    private void RebuildCalendar()
    {
        RebuildMonth(LeftMonth, LeftDays);
        RebuildMonth(RightMonth, RightDays);
    }

    private void RebuildMonth(DateTime monthFirst, ObservableCollection<DayCell> target)
    {
        target.Clear();
        // 第一天是週幾（Sunday=0 … Saturday=6）
        int leadDays = (int)monthFirst.DayOfWeek;
        var gridStart = monthFirst.AddDays(-leadDays);
        var today = DateTime.Today;

        for (int i = 0; i < 42; i++)
        {
            var d = gridStart.AddDays(i);
            var isCurrent = d.Month == monthFirst.Month;
            var isStart = StartDate is { } s && d.Date == s.Date;
            var isEnd = EndDate is { } en && d.Date == en.Date;
            var inRange = StartDate is { } ss && EndDate is { } ee && d.Date > ss.Date && d.Date < ee.Date;
            target.Add(new DayCell
            {
                Date = d,
                Day = d.Day.ToString(),
                IsCurrentMonth = isCurrent,
                IsToday = d.Date == today,
                IsRangeStart = isStart,
                IsRangeEnd = isEnd,
                IsInRange = inRange,
            });
        }
    }

    // ── INotifyPropertyChanged ──────────────────────────────

    public event PropertyChangedEventHandler? PropertyChanged;
    private void RaisePropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>日曆中單一日格。用 public/sealed 以便 XAML DataTemplate 綁定。</summary>
public sealed class DayCell
{
    public DateTime Date { get; init; }
    public string Day { get; init; } = "";
    public bool IsCurrentMonth { get; init; }
    public bool IsToday { get; init; }
    public bool IsRangeStart { get; init; }
    public bool IsRangeEnd { get; init; }
    public bool IsInRange { get; init; }
}
