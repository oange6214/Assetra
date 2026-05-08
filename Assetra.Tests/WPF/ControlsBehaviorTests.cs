using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using Assetra.WPF.Controls;
using Assetra.WPF.Infrastructure.Converters;
using Assetra.WPF.Infrastructure.Behaviors;
using Xunit;

namespace Assetra.Tests.WPF;

public sealed class ControlsBehaviorTests
{
    [Fact]
    public void DateRangePicker_TypedStartAfterEndClearsEndDate()
    {
        StaRun(() =>
        {
            EnsureResources();
            var picker = new DateRangePicker
            {
                StartDate = new DateTime(2026, 1, 1),
                EndDate = new DateTime(2026, 1, 31),
            };

            picker.StartDateText = "2026-02-01";

            Assert.Equal(new DateTime(2026, 2, 1), picker.StartDate);
            Assert.Null(picker.EndDate);
        });
    }

    [Fact]
    public void DateRangePicker_TypedEndBeforeStartClearsStartDate()
    {
        StaRun(() =>
        {
            EnsureResources();
            var picker = new DateRangePicker
            {
                StartDate = new DateTime(2026, 2, 1),
                EndDate = new DateTime(2026, 2, 28),
            };

            picker.EndDateText = "2026-01-15";

            Assert.Null(picker.StartDate);
            Assert.Equal(new DateTime(2026, 1, 15), picker.EndDate);
        });
    }

    [Fact]
    public void HexColorPicker_PresetsBindingWorksInsidePopup()
    {
        StaRun(() =>
        {
            EnsureResources();
            var picker = new HexColorPicker
            {
                Presets = new[] { "#112233", "#445566" },
            };

            var itemsControl = GetPrivateField<ItemsControl>(picker, "PresetsItemsControl");

            Assert.NotNull(itemsControl);
            Assert.Same(picker.Presets, itemsControl.ItemsSource);
        });
    }

    [Fact]
    public void DatePickerDateOnlyBehavior_NormalizesTextAndSelectedDate()
    {
        StaRun(() =>
        {
            var picker = new DatePicker
            {
                SelectedDate = new DateTime(2026, 5, 6, 23, 15, 0),
            };

            picker.ApplyTemplate();
            DatePickerDateOnlyBehavior.SetIsEnabled(picker, true);

            Assert.Equal(new DateTime(2026, 5, 6), picker.SelectedDate);
            Assert.Equal(new DateTime(2026, 5, 6), picker.DisplayDate.Date);
        });
    }

    [Fact]
    public void DatePickerDateOnlyBehavior_AllowsFutureDatesByDefault()
    {
        StaRun(() =>
        {
            var futureDate = DateTime.Today.AddDays(7).Date;
            var picker = new DatePicker();

            picker.ApplyTemplate();
            DatePickerDateOnlyBehavior.SetIsEnabled(picker, true);
            picker.SelectedDate = futureDate.AddHours(12);

            Assert.Equal(futureDate, picker.SelectedDate);
            Assert.Equal(futureDate, picker.DisplayDate.Date);
        });
    }

    [Fact]
    public void DatePickerDateOnlyBehavior_PastOnlySetsDisplayDateEndToToday()
    {
        StaRun(() =>
        {
            var picker = new DatePicker();

            DatePickerDateOnlyBehavior.SetConstraint(picker, DatePickerDateConstraint.PastOnly);
            DatePickerDateOnlyBehavior.SetIsEnabled(picker, true);

            Assert.Equal(DateTime.Today.Date, picker.DisplayDateEnd?.Date);
        });
    }

    [Fact]
    public void DatePickerDateOnlyBehavior_FutureOnlySetsDisplayDateStartToToday()
    {
        StaRun(() =>
        {
            var picker = new DatePicker();

            DatePickerDateOnlyBehavior.SetConstraint(picker, DatePickerDateConstraint.FutureOnly);
            DatePickerDateOnlyBehavior.SetIsEnabled(picker, true);

            Assert.Equal(DateTime.Today.Date, picker.DisplayDateStart?.Date);
            Assert.Null(picker.DisplayDateEnd);
        });
    }

    [Fact]
    public void DatePickerDateOnlyBehavior_DoesNotInterruptYearSelectionMode()
    {
        StaRun(() =>
        {
            var picker = new DatePicker
            {
                SelectedDate = new DateTime(2026, 5, 8),
            };

            var window = new Window
            {
                Content = picker,
                Width = 320,
                Height = 240,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow,
            };

            try
            {
                window.Show();
                DatePickerDateOnlyBehavior.SetIsEnabled(picker, true);
                picker.IsDropDownOpen = true;
                PumpDispatcher();

                var calendar = GetPopupCalendar(picker);
                Assert.NotNull(calendar);

                calendar!.DisplayMode = CalendarMode.Decade;
                picker.SelectedDate = new DateTime(2026, 5, 9);
                PumpDispatcher();

                Assert.Equal(CalendarMode.Decade, calendar.DisplayMode);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void ThousandSeparatorBehavior_FormatsMoneyTextAsUserTypes()
    {
        StaRun(() =>
        {
            var textBox = new TextBox();

            ThousandSeparatorBehavior.SetIsEnabled(textBox, true);
            textBox.Text = "22211";

            Assert.Equal("22,211", textBox.Text);

            textBox.Text = "-1234567.89";

            Assert.Equal("-1,234,567.89", textBox.Text);
        });
    }

    [Fact]
    public void ThousandSeparatorBehavior_AlignsNumericInputToTheRight()
    {
        StaRun(() =>
        {
            var textBox = new TextBox();

            ThousandSeparatorBehavior.SetIsEnabled(textBox, true);

            Assert.Equal(HorizontalAlignment.Right, textBox.HorizontalContentAlignment);
        });
    }

    private static void StaRun(Action action)
    {
        Exception? caught = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { caught = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (caught is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(caught).Throw();
    }

    private static void EnsureResources()
    {
        var app = System.Windows.Application.Current ?? new System.Windows.Application();
        var resources = app.Resources;

        AddMissing(resources, "FocusVisual", new Style(typeof(Control)));
        AddMissing(resources, "Radius.Sm", new CornerRadius(4));
        AddMissing(resources, "Radius.Md", new CornerRadius(6));
        AddMissing(resources, "Font.Family.Base", new FontFamily("Segoe UI"));
        AddMissing(resources, "Font.Size.Xs", 11d);
        AddMissing(resources, "Font.Size.Sm", 12d);
        AddMissing(resources, "Font.Size.Base", 14d);
        AddMissing(resources, "BooleanToVisibilityConverter", new BooleanToVisibilityConverter());
        AddMissing(resources, "HexToBrushConverter", new HexToBrushConverter());
    }

    private static void AddMissing(ResourceDictionary resources, string key, object? value)
    {
        if (!resources.Contains(key))
            resources.Add(key, value);
    }

    private static T GetPrivateField<T>(object instance, string name)
    {
        var field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return Assert.IsType<T>(field!.GetValue(instance));
    }

    private static Calendar? GetPopupCalendar(DatePicker picker)
    {
        picker.ApplyTemplate();
        if (picker.Template?.FindName("PART_Popup", picker) is not Popup popup)
            return null;

        return popup.Child switch
        {
            Calendar calendar => calendar,
            DependencyObject child => FindVisualChild<Calendar>(child),
            _ => null,
        };
    }

    private static T? FindVisualChild<T>(DependencyObject root)
        where T : DependencyObject
    {
        if (root is T match)
            return match;

        var children = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < children; i++)
        {
            var found = FindVisualChild<T>(VisualTreeHelper.GetChild(root, i));
            if (found is not null)
                return found;
        }

        return null;
    }

    private static void PumpDispatcher()
    {
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    }

}
