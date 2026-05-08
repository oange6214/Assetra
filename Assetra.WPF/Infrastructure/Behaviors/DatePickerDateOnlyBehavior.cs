using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;

namespace Assetra.WPF.Infrastructure.Behaviors;

public enum DatePickerDateConstraint
{
    None,
    PastOnly,
    FutureOnly,
    Range,
}

/// <summary>
/// Keeps WPF DatePicker values date-only and displays selected dates as yyyy-MM-dd.
/// </summary>
public static class DatePickerDateOnlyBehavior
{
    private const double MinimumExpandedCalendarViewHeight = 214d;

    private static readonly DependencyProperty IsCalendarRepairAttachedProperty =
        DependencyProperty.RegisterAttached(
            "IsCalendarRepairAttached",
            typeof(bool),
            typeof(DatePickerDateOnlyBehavior),
            new PropertyMetadata(false));

    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(DatePickerDateOnlyBehavior),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static readonly DependencyProperty AllowFutureDatesProperty =
        DependencyProperty.RegisterAttached(
            "AllowFutureDates",
            typeof(bool),
            typeof(DatePickerDateOnlyBehavior),
            new PropertyMetadata(true, OnAllowFutureDatesChanged));

    public static readonly DependencyProperty ConstraintProperty =
        DependencyProperty.RegisterAttached(
            "Constraint",
            typeof(DatePickerDateConstraint),
            typeof(DatePickerDateOnlyBehavior),
            new PropertyMetadata(DatePickerDateConstraint.None, OnConstraintChanged));

    [ThreadStatic]
    private static bool _isNormalizing;

    public static bool GetIsEnabled(DependencyObject d) => (bool)d.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject d, bool value) => d.SetValue(IsEnabledProperty, value);

    public static bool GetAllowFutureDates(DependencyObject d) => (bool)d.GetValue(AllowFutureDatesProperty);
    public static void SetAllowFutureDates(DependencyObject d, bool value) => d.SetValue(AllowFutureDatesProperty, value);

    public static DatePickerDateConstraint GetConstraint(DependencyObject d) => (DatePickerDateConstraint)d.GetValue(ConstraintProperty);
    public static void SetConstraint(DependencyObject d, DatePickerDateConstraint value) => d.SetValue(ConstraintProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DatePicker picker)
            return;

        picker.Loaded -= OnLoaded;
        picker.SelectedDateChanged -= OnSelectedDateChanged;
        picker.CalendarOpened -= OnCalendarOpened;

        if ((bool)e.NewValue)
        {
            picker.Loaded += OnLoaded;
            picker.SelectedDateChanged += OnSelectedDateChanged;
            picker.CalendarOpened += OnCalendarOpened;
            Normalize(picker);
        }
    }

    private static void OnAllowFutureDatesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DatePicker picker && GetIsEnabled(picker))
            Normalize(picker);
    }

    private static void OnConstraintChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DatePicker picker && GetIsEnabled(picker))
            Normalize(picker);
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is DatePicker picker)
            Normalize(picker);
    }

    private static void OnSelectedDateChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is DatePicker picker)
            Normalize(picker);
    }

    private static void OnCalendarOpened(object? sender, RoutedEventArgs e)
    {
        if (sender is not DatePicker picker)
            return;

        Normalize(picker);
        if (FindPopupCalendar(picker) is { } calendar)
        {
            EnsureCalendarRepairAttached(calendar);
            QueueCalendarViewRepair(calendar);
        }

        SyncPopupCalendar(picker);
        QueuePopupCalendarSync(picker);
    }

    private static void Normalize(DatePicker picker)
    {
        if (_isNormalizing)
            return;

        _isNormalizing = true;
        try
        {
            ApplyDefaultDateRange(picker);

            if (picker.SelectedDate is not { } selectedDate)
                return;

            var dateOnly = selectedDate.Date;
            if (selectedDate != dateOnly)
                picker.SetCurrentValue(DatePicker.SelectedDateProperty, dateOnly);

            if (IsWithinConfiguredRange(picker, dateOnly))
                picker.SetCurrentValue(DatePicker.DisplayDateProperty, dateOnly);

            ApplyFormattedText(picker, dateOnly);

            if (picker.IsDropDownOpen)
                QueuePopupCalendarSync(picker);
        }
        finally
        {
            _isNormalizing = false;
        }
    }

    private static void ApplyDefaultDateRange(DatePicker picker)
    {
        var today = DateTime.Today.Date;
        var constraint = GetEffectiveConstraint(picker);

        switch (constraint)
        {
            case DatePickerDateConstraint.PastOnly:
                if (picker.ReadLocalValue(DatePicker.DisplayDateEndProperty) == DependencyProperty.UnsetValue)
                    picker.SetCurrentValue(DatePicker.DisplayDateEndProperty, today);
                break;
            case DatePickerDateConstraint.FutureOnly:
                if (picker.ReadLocalValue(DatePicker.DisplayDateStartProperty) == DependencyProperty.UnsetValue)
                    picker.SetCurrentValue(DatePicker.DisplayDateStartProperty, today);
                if (picker.ReadLocalValue(DatePicker.DisplayDateEndProperty) == DependencyProperty.UnsetValue)
                    picker.SetCurrentValue(DatePicker.DisplayDateEndProperty, null);
                break;
            case DatePickerDateConstraint.Range:
                break;
            default:
                if (picker.ReadLocalValue(DatePicker.DisplayDateEndProperty) == DependencyProperty.UnsetValue)
                    picker.SetCurrentValue(DatePicker.DisplayDateEndProperty, null);
                break;
        }
    }

    private static DatePickerDateConstraint GetEffectiveConstraint(DatePicker picker)
    {
        var constraint = GetConstraint(picker);
        if (constraint == DatePickerDateConstraint.None && !GetAllowFutureDates(picker))
            return DatePickerDateConstraint.PastOnly;
        return constraint;
    }

    private static void SyncPopupCalendar(DatePicker picker)
    {
        if (picker.SelectedDate is not { } selectedDate)
            return;

        var dateOnly = selectedDate.Date;
        if (!IsWithinConfiguredRange(picker, dateOnly))
            return;

        var calendar = FindPopupCalendar(picker);
        if (calendar is null)
            return;

        EnsureCalendarRepairAttached(calendar);
        RepairCalendarDisplayMode(calendar);

        if (calendar.DisplayMode != CalendarMode.Month)
            return;

        calendar.SetCurrentValue(Calendar.DisplayDateProperty, dateOnly);
        calendar.SetCurrentValue(Calendar.SelectedDateProperty, dateOnly);
    }

    private static Calendar? FindPopupCalendar(DatePicker picker)
    {
        picker.ApplyTemplate();

        if (picker.Template?.FindName("PART_Calendar", picker) is Calendar calendar)
            return calendar;

        if (picker.Template?.FindName("PART_Popup", picker) is not Popup popup)
            return null;

        if (popup.Child is Calendar popupCalendar)
            return popupCalendar;

        return popup.Child is DependencyObject child
            ? FindDescendant<Calendar>(child)
            : null;
    }

    private static T? FindDescendant<T>(DependencyObject root)
        where T : DependencyObject
    {
        if (root is T match)
            return match;

        var visualChildren = root is Visual or Visual3D
            ? VisualTreeHelper.GetChildrenCount(root)
            : 0;

        for (var i = 0; i < visualChildren; i++)
        {
            var found = FindDescendant<T>(VisualTreeHelper.GetChild(root, i));
            if (found is not null)
                return found;
        }

        foreach (var logicalChild in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            var found = FindDescendant<T>(logicalChild);
            if (found is not null)
                return found;
        }

        return null;
    }

    private static void EnsureCalendarRepairAttached(Calendar calendar)
    {
        if ((bool)calendar.GetValue(IsCalendarRepairAttachedProperty))
            return;

        calendar.DisplayModeChanged += OnCalendarDisplayModeChanged;
        calendar.SetValue(IsCalendarRepairAttachedProperty, true);
    }

    private static void OnCalendarDisplayModeChanged(object? sender, CalendarModeChangedEventArgs e)
    {
        if (sender is Calendar calendar)
            QueueCalendarViewRepair(calendar);
    }

    private static void QueueCalendarViewRepair(Calendar calendar)
    {
        _ = calendar.Dispatcher.BeginInvoke(
            () => RepairCalendarDisplayMode(calendar),
            DispatcherPriority.ContextIdle);
    }

    private static void RepairCalendarDisplayMode(Calendar calendar)
    {
        calendar.ApplyTemplate();

        var item = FindDescendant<CalendarItem>(calendar);
        if (item is null)
            return;

        item.ApplyTemplate();

        if (item.Template?.FindName("PART_MonthView", item) is not FrameworkElement monthView
            || item.Template?.FindName("PART_YearView", item) is not FrameworkElement yearView)
        {
            return;
        }

        var isMonthMode = calendar.DisplayMode == CalendarMode.Month;
        var desiredMonthVisibility = isMonthMode ? Visibility.Visible : Visibility.Hidden;
        var desiredYearVisibility = isMonthMode ? Visibility.Hidden : Visibility.Visible;

        calendar.SetCurrentValue(FrameworkElement.HeightProperty, double.NaN);
        yearView.SetCurrentValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
        yearView.SetCurrentValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Stretch);
        yearView.SetCurrentValue(
            FrameworkElement.HeightProperty,
            isMonthMode ? double.NaN : ResolveExpandedCalendarViewHeight(monthView, item));

        if (monthView.Visibility != desiredMonthVisibility)
            monthView.SetCurrentValue(UIElement.VisibilityProperty, desiredMonthVisibility);

        if (yearView.Visibility != desiredYearVisibility)
            yearView.SetCurrentValue(UIElement.VisibilityProperty, desiredYearVisibility);
    }

    private static double ResolveExpandedCalendarViewHeight(FrameworkElement monthView, FrameworkElement calendarItem)
    {
        var height = monthView.ActualHeight;
        if (height <= 0)
            height = monthView.DesiredSize.Height;
        if (height <= 0)
            height = calendarItem.ActualHeight;

        return Math.Max(MinimumExpandedCalendarViewHeight, height);
    }

    private static void QueuePopupCalendarSync(DatePicker picker)
    {
        _ = picker.Dispatcher.BeginInvoke(
            () =>
            {
                if (GetIsEnabled(picker) && picker.IsDropDownOpen)
                    SyncPopupCalendar(picker);
            },
            DispatcherPriority.Loaded);
    }

    private static bool IsWithinConfiguredRange(DatePicker picker, DateTime date)
    {
        if (picker.DisplayDateStart is { } start && date < start.Date)
            return false;

        if (picker.DisplayDateEnd is { } end && date > end.Date)
            return false;

        return true;
    }

    private static void ApplyFormattedText(DatePicker picker, DateTime dateOnly)
    {
        var formatted = dateOnly.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        picker.Text = formatted;
        ApplyFormattedTextToTemplate(picker, formatted);

        _ = picker.Dispatcher.BeginInvoke(
            () =>
            {
                if (GetIsEnabled(picker) && picker.SelectedDate?.Date == dateOnly)
                    ApplyFormattedTextToTemplate(picker, formatted);
            },
            DispatcherPriority.ContextIdle);
    }

    private static void ApplyFormattedTextToTemplate(DatePicker picker, string formatted)
    {
        picker.ApplyTemplate();

        if (picker.Template?.FindName("PART_TextBox", picker) is DatePickerTextBox textBox
            && textBox.Text != formatted)
        {
            textBox.Text = formatted;
        }
    }
}
