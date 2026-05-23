using System.Reflection;
using System.Collections.Generic;
using System.Linq;
using System.IO;
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
                var (monthView, yearView) = GetCalendarViews(calendar);
                Assert.NotNull(monthView);
                Assert.NotNull(yearView);
                Assert.NotEqual(Visibility.Visible, monthView!.Visibility);
                Assert.Equal(Visibility.Visible, yearView!.Visibility);
                Assert.True(double.IsNaN(calendar.Height));

                var yearElement = Assert.IsAssignableFrom<FrameworkElement>(yearView);
                Assert.Equal(HorizontalAlignment.Stretch, yearElement.HorizontalAlignment);
                Assert.Equal(VerticalAlignment.Stretch, yearElement.VerticalAlignment);
                Assert.True(yearElement.Height >= 214d);
                Assert.True(yearElement.Width >= 240d);
                if (yearElement is Grid yearGrid)
                {
                    Assert.All(yearGrid.ColumnDefinitions, column =>
                        Assert.Equal(GridUnitType.Star, column.Width.GridUnitType));
                    Assert.All(yearGrid.RowDefinitions, row =>
                        Assert.Equal(GridUnitType.Star, row.Height.GridUnitType));
                }

                var yearButtons = FindVisualChildren<CalendarButton>(yearElement).ToList();
                Assert.NotEmpty(yearButtons);
                Assert.All(yearButtons, button =>
                {
                    Assert.Equal(HorizontalAlignment.Stretch, button.HorizontalAlignment);
                    Assert.Equal(VerticalAlignment.Stretch, button.VerticalAlignment);
                    Assert.Equal(HorizontalAlignment.Center, button.HorizontalContentAlignment);
                    Assert.Equal(VerticalAlignment.Center, button.VerticalContentAlignment);
                });

                yearButtons[0].ApplyTemplate();
                var contentPresenter = FindVisualChild<ContentPresenter>(yearButtons[0]);
                Assert.NotNull(contentPresenter);
                Assert.Equal(HorizontalAlignment.Center, contentPresenter!.HorizontalAlignment);
                Assert.Equal(VerticalAlignment.Center, contentPresenter.VerticalAlignment);

                calendar.DisplayMode = CalendarMode.Month;
                PumpDispatcher();

                Assert.True(double.IsNaN(calendar.Height));
                Assert.True(double.IsNaN(yearElement.Height));
                Assert.True(double.IsNaN(yearElement.Width));
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

    [Fact]
    public void BuyTxForm_ReadOnlyDisplayBindingsAreOneWay()
    {
        var xamlPath = GetBuyTxFormPath();
        var xaml = File.ReadAllText(xamlPath);

        Assert.Contains("Text=\"{Binding Buy.SettlementPairDisplay, Mode=OneWay}\"", xaml);
        Assert.Contains("Text=\"{Binding Buy.FxRateDateDisplay, Mode=OneWay}\"", xaml);
        Assert.Contains("Text=\"{Binding Buy.FxSourceDisplay, Mode=OneWay}\"", xaml);
    }

    [Fact]
    public void BuyTxForm_FxRefreshActionDoesNotFillSectionHeader()
    {
        var xamlPath = GetBuyTxFormPath();
        var xaml = File.ReadAllText(xamlPath);

        Assert.Contains("Command=\"{Binding FetchBuyFxRateCommand}\"", xaml);
        Assert.Contains("MinWidth=\"0\"", xaml);
        Assert.Contains("MinHeight=\"28\"", xaml);
        Assert.Contains("Visibility=\"{Binding Buy.IsFxSettlementMode", xaml);
    }

    [Fact]
    public void TransactionFxCopy_UsesCashSettlementVocabulary()
    {
        var zh = File.ReadAllText(GetLanguagePath("zh-TW.xaml"));
        var en = File.ReadAllText(GetLanguagePath("en-US.xaml"));

        Assert.Contains("成交金額輸入方式", zh);
        Assert.Contains("每股價格", zh);
        Assert.Contains("成交總額", zh);
        Assert.Contains("帳戶扣款", zh);
        Assert.Contains("實際扣款金額", zh);
        Assert.Contains("使用帳戶明細金額", zh);
        Assert.Contains("用匯率估算", zh);
        Assert.Contains("此區只影響資金帳戶流水，不改變上方成交金額。", zh);
        Assert.DoesNotContain(">結算與匯率<", zh);
        Assert.DoesNotContain(">扣款與匯率<", zh);
        Assert.DoesNotContain(">依明細金額<", zh);
        Assert.DoesNotContain(">複委託請填<", zh);
        Assert.DoesNotContain("跨幣別交易（複委託）", zh);

        Assert.Contains("Trade amount input", en);
        Assert.Contains("Per-share price", en);
        Assert.Contains("Trade total", en);
        Assert.Contains("Account debit", en);
        Assert.Contains("Actual debit amount", en);
        Assert.Contains("Use account statement amount", en);
        Assert.Contains("FX estimate", en);
        Assert.Contains("This section only affects the cash-account movement, not the trade amount above.", en);
        Assert.DoesNotContain(">Settlement and FX<", en);
        Assert.DoesNotContain(">Cash settlement and FX<", en);
        Assert.DoesNotContain(">Required for sub-brokerage<", en);
    }

    [Fact]
    public void BuyTxForm_CrossCurrencySettlementUsesExplicitInputMode()
    {
        var xaml = File.ReadAllText(GetBuyTxFormPath());

        Assert.Contains("Text=\"{DynamicResource Portfolio.Tx.SettlementInputMode}\"", xaml);
        Assert.Contains("IsChecked=\"{Binding Buy.SettlementInputMode, Converter={StaticResource EqualityConverter}, ConverterParameter=statement, Mode=TwoWay}\"", xaml);
        Assert.Contains("IsChecked=\"{Binding Buy.SettlementInputMode, Converter={StaticResource EqualityConverter}, ConverterParameter=fx, Mode=TwoWay}\"", xaml);
        Assert.Contains("Visibility=\"{Binding Buy.IsStatementSettlementMode, Converter={StaticResource BooleanToVisibilityConverter}}\"", xaml);
        Assert.Contains("Visibility=\"{Binding Buy.IsFxSettlementMode, Converter={StaticResource BooleanToVisibilityConverter}}\"", xaml);
    }

    [Fact]
    public void BuyTxForm_CashSettlementDoesNotUseBadgeForRequiredAmount()
    {
        var xaml = File.ReadAllText(GetBuyTxFormPath());

        Assert.DoesNotContain("Portfolio.Tx.ActualCashAmount.RequiredBadge", xaml);
        Assert.DoesNotContain("Background=\"{DynamicResource AppDangerSubtle}\"", xaml);
        Assert.DoesNotContain("Foreground=\"{DynamicResource AppDanger}\"", xaml);
        Assert.Contains("Text=\"{DynamicResource Portfolio.Tx.ActualCashAmount}\"", xaml);
    }

    [Fact]
    public void CrossCurrencyTxForms_UseSharedSettlementBadgeStyle()
    {
        var sell = File.ReadAllText(GetTxFormPath("SellTxForm.xaml"));
        var dividend = File.ReadAllText(GetTxFormPath("CashDividendTxForm.xaml"));

        Assert.Contains("Style=\"{StaticResource StatusBadge.Info}\"", sell);
        Assert.Contains("Style=\"{StaticResource StatusBadge.Info}\"", dividend);
        Assert.DoesNotContain("Background=\"{DynamicResource AppDangerSubtle}\"", sell);
        Assert.DoesNotContain("Foreground=\"{DynamicResource AppDanger}\"", sell);
        Assert.DoesNotContain("Background=\"{DynamicResource AppDangerSubtle}\"", dividend);
        Assert.DoesNotContain("Foreground=\"{DynamicResource AppDanger}\"", dividend);
    }

    private static string GetBuyTxFormPath() => GetTxFormPath("BuyTxForm.xaml");

    private static string GetTxFormPath(string fileName)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(
                current.FullName,
                "Assetra.WPF",
                "Features",
                "Portfolio",
                "Controls",
                "TxForms",
                fileName);
            if (File.Exists(candidate))
                return candidate;

            current = current.Parent;
        }

        throw new FileNotFoundException($"Could not locate transaction form {fileName}.");
    }

    private static string GetLanguagePath(string fileName)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(
                current.FullName,
                "Assetra.WPF",
                "Languages",
                fileName);
            if (File.Exists(candidate))
                return candidate;

            current = current.Parent;
        }

        throw new FileNotFoundException($"Could not locate language dictionary {fileName}.");
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

    private static (UIElement? MonthView, UIElement? YearView) GetCalendarViews(Calendar calendar)
    {
        calendar.ApplyTemplate();
        var item = FindVisualChild<CalendarItem>(calendar);
        Assert.NotNull(item);
        item!.ApplyTemplate();

        var monthView = item.Template?.FindName("PART_MonthView", item) as UIElement;
        var yearView = item.Template?.FindName("PART_YearView", item) as UIElement;
        return (monthView, yearView);
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

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root)
        where T : DependencyObject
    {
        if (root is T match)
            yield return match;

        var children = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < children; i++)
        {
            foreach (var child in FindVisualChildren<T>(VisualTreeHelper.GetChild(root, i)))
                yield return child;
        }
    }

    private static void PumpDispatcher()
    {
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    }

}
