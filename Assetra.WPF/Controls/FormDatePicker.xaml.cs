using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Assetra.WPF.Controls;

/// <summary>
/// 自製 DateTimePicker UserControl：TextBox + 日曆圖示 + Popup（自製 CalendarPanel）。
/// 顯示格式 yyyy-MM-dd HH:mm，日曆選取日期時保留原本的時間部分。
/// </summary>
public partial class FormDatePicker : UserControl
{
    // Dependency Properties

    public static readonly DependencyProperty SelectedDateProperty =
        DependencyProperty.Register(
            nameof(SelectedDate),
            typeof(DateTime?),
            typeof(FormDatePicker),
            new FrameworkPropertyMetadata(
                null,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnSelectedDateChanged));

    public static readonly DependencyProperty DisplayDateEndProperty =
        DependencyProperty.Register(
            nameof(DisplayDateEnd),
            typeof(DateTime?),
            typeof(FormDatePicker),
            new PropertyMetadata(null, OnDisplayDateEndChanged));

    /// <summary>
    /// When true, displays and parses "yyyy-MM-dd HH:mm".
    /// When false (default), displays and parses "yyyy-MM-dd" only.
    /// </summary>
    public static readonly DependencyProperty ShowTimeProperty =
        DependencyProperty.Register(
            nameof(ShowTime),
            typeof(bool),
            typeof(FormDatePicker),
            new PropertyMetadata(false, (d, _) => ((FormDatePicker)d).UpdateTextFromDate(((FormDatePicker)d).SelectedDate)));

    // CLR wrappers

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

    public bool ShowTime
    {
        get => (bool)GetValue(ShowTimeProperty);
        set => SetValue(ShowTimeProperty, value);
    }

    // Private state

    private bool _isUpdating;

    // Constructor

    public FormDatePicker()
    {
        InitializeComponent();
        UpdateTextFromDate(SelectedDate);
    }

    // Property-changed callbacks

    private static void OnSelectedDateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FormDatePicker picker)
            picker.UpdateTextFromDate(e.NewValue as DateTime?);
    }

    private static void OnDisplayDateEndChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FormDatePicker picker)
            picker.InnerCalendar.DisplayDateEnd = e.NewValue as DateTime?;
    }

    // Format helpers

    /// <summary>
    /// DisplayDateEnd 限制的是「日期」，不是「日期+時間」。
    /// DateTime.Today = 2026-04-12 00:00，但當天 09:30 仍然合法。
    /// 這個方法把 DisplayDateEnd 擴展到當天的 23:59:59。
    /// </summary>
    private DateTime? EffectiveMaxDateTime =>
        DisplayDateEnd.HasValue ? DisplayDateEnd.Value.Date.AddDays(1).AddTicks(-1) : null;

    private DateTime ClampToMax(DateTime dt)
    {
        var max = EffectiveMaxDateTime;
        return max.HasValue && dt > max.Value ? max.Value : dt;
    }

    private string DisplayFormat => ShowTime ? "yyyy-MM-dd HH:mm" : "yyyy-MM-dd";

    private static readonly string[] DateOnlyFormats =
    [
        "yyyy-MM-dd", "yyyy/MM/dd", "yyyyMMdd",
        "MM/dd/yyyy", "dd/MM/yyyy",
        "yyyy-M-d",   "yyyy/M/d",
    ];

    private static readonly string[] DateTimeFormats =
    [
        "yyyy-MM-dd HH:mm", "yyyy-MM-dd H:mm",
        "yyyy/MM/dd HH:mm", "yyyy/MM/dd H:mm",
        "yyyy-MM-dd HH:mm:ss",
        // date-only formats also accepted — time defaults to 00:00
        "yyyy-MM-dd", "yyyy/MM/dd", "yyyyMMdd",
        "MM/dd/yyyy", "dd/MM/yyyy",
        "yyyy-M-d",   "yyyy/M/d",
    ];

    private string[] ActiveFormats => ShowTime ? DateTimeFormats : DateOnlyFormats;

    private void UpdateTextFromDate(DateTime? date)
    {
        if (_isUpdating)
            return;
        DateTextBox.Text = date.HasValue
            ? date.Value.ToString(DisplayFormat, CultureInfo.InvariantCulture)
            : string.Empty;
    }

    private void CommitTextBoxDate()
    {
        var text = DateTextBox.Text.Trim();

        // 空白 → 還原顯示，不清空
        if (string.IsNullOrEmpty(text))
        {
            UpdateTextFromDate(SelectedDate);
            return;
        }

        if (DateTime.TryParseExact(text, ActiveFormats,
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
        {
            dt = ClampToMax(dt);
            _isUpdating = true;
            SelectedDate = dt;
            _isUpdating = false;
            DateTextBox.Text = dt.ToString(DisplayFormat, CultureInfo.InvariantCulture);
        }
        else
        {
            UpdateTextFromDate(SelectedDate);
        }
    }

    // Event handlers

    private void InnerCalendar_DatePicked(object sender, DateTime date)
    {
        var existing = SelectedDate;
        var time = ShowTime && existing.HasValue ? existing.Value.TimeOfDay : TimeSpan.Zero;
        var combined = ClampToMax(date.Date.Add(time));

        _isUpdating = true;
        SelectedDate = combined;
        _isUpdating = false;
        UpdateTextFromDate(combined);

        if (!ShowTime)
            CalendarPopup.IsOpen = false;
    }

    private void InnerTime_TimePicked(object sender, TimeSpan time)
    {
        var date = SelectedDate?.Date ?? DateTime.Today;
        var combined = ClampToMax(date.Add(time));

        _isUpdating = true;
        SelectedDate = combined;
        _isUpdating = false;
        UpdateTextFromDate(combined);

        CalendarPopup.IsOpen = false;
    }

    private void ToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (CalendarPopup.IsOpen)
        { CalendarPopup.IsOpen = false; return; }

        // 同步日曆面板
        InnerCalendar.SelectedDate = SelectedDate;

        // 同步時間面板（ShowTime 時才顯示）
        TimePanelBorder.Visibility = ShowTime ? Visibility.Visible : Visibility.Collapsed;
        if (ShowTime)
        {
            // 如果尚未選過時間（00:00），預設捲動到目前最近的時間
            var time = SelectedDate?.TimeOfDay ?? TimeSpan.Zero;
            if (time == TimeSpan.Zero)
                time = DateTime.Now.TimeOfDay;
            InnerTime.SelectedTime = time;
            Dispatcher.BeginInvoke(() => InnerTime.ScrollToSelected());
        }

        CalendarPopup.IsOpen = true;
    }

    private void CalendarPopup_Opened(object sender, EventArgs e)
    {
        // Fade in + slide down from slightly above
        PopupContent.Opacity = 0;
        PopupSlide.Y = -8;

        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = new Duration(TimeSpan.FromMilliseconds(180));

        PopupContent.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, duration));

        PopupSlide.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(-8, 0, duration) { EasingFunction = easing });
    }

    private void CalendarPopup_Closed(object sender, EventArgs e)
    {
        DateTextBox.Focus();
    }

    private void DateTextBox_LostFocus(object sender, RoutedEventArgs e) => CommitTextBoxDate();

    private void DateTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter || e.Key == Key.Tab)
        {
            CommitTextBoxDate();
            e.Handled = e.Key == Key.Enter;
        }
        else if (e.Key == Key.Escape)
        {
            UpdateTextFromDate(SelectedDate);
        }
    }

    // Focus → highlight outer border

    protected override void OnGotFocus(RoutedEventArgs e)
    {
        base.OnGotFocus(e);
        OuterBorder.SetResourceReference(Border.BorderBrushProperty, "AppAccent");
    }

    protected override void OnLostFocus(RoutedEventArgs e)
    {
        base.OnLostFocus(e);
        if (!IsKeyboardFocusWithin)
            OuterBorder.SetResourceReference(Border.BorderBrushProperty, "AppBorder");
    }
}
