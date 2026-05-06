using System.ComponentModel;
using System.Windows;
using Wpf.Ui.Controls;

namespace Assetra.WPF.Infrastructure.Behaviors;

/// <summary>
/// Keeps CalendarDatePicker.Date at midnight so WPF Calendar can match and
/// highlight the selected day.
/// </summary>
public static class CalendarDatePickerDateOnlyBehavior
{
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(CalendarDatePickerDateOnlyBehavior),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject d) => (bool)d.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject d, bool value) => d.SetValue(IsEnabledProperty, value);

    [ThreadStatic]
    private static bool _isNormalizing;

    private static readonly DependencyPropertyDescriptor? DateDescriptor =
        DependencyPropertyDescriptor.FromProperty(CalendarDatePicker.DateProperty, typeof(CalendarDatePicker));

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not CalendarDatePicker picker)
            return;

        picker.Loaded -= OnLoaded;
        DateDescriptor?.RemoveValueChanged(picker, OnDateChanged);

        if ((bool)e.NewValue)
        {
            picker.Loaded += OnLoaded;
            DateDescriptor?.AddValueChanged(picker, OnDateChanged);
            Normalize(picker);
        }
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is CalendarDatePicker picker)
            Normalize(picker);
    }

    private static void OnDateChanged(object? sender, EventArgs e)
    {
        if (sender is CalendarDatePicker picker)
            Normalize(picker);
    }

    private static void Normalize(CalendarDatePicker picker)
    {
        if (_isNormalizing || picker.Date is not { } date || date == date.Date)
            return;

        _isNormalizing = true;
        try
        {
            picker.SetCurrentValue(CalendarDatePicker.DateProperty, date.Date);
        }
        finally
        {
            _isNormalizing = false;
        }
    }
}
