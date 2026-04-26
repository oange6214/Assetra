using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Assetra.WPF.Features.Portfolio.Controls;

public partial class DividendCalendarPanel : UserControl
{
    public DividendCalendarPanel()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Rebuild();
        Loaded += (_, _) => Rebuild();
    }

    public void Rebuild()
    {
        if (!IsLoaded || DataContext is not PortfolioViewModel vm)
            return;

        MonthGrid.Children.Clear();
        var year = vm.DivCalendar.Year;
        var monthlyTotals = vm.DivCalendar.GetDividendsByMonth(year);
        var hasDividends = monthlyTotals.Count > 0;
        var yearTotal = monthlyTotals.Values.Sum();

        // Update year total display
        YearTotalText.Text = hasDividends ? $"合計 {yearTotal:N0}" : string.Empty;

        // Hide entire panel if no dividend trades exist at all
        var anyDivTradesEver = vm.Trades.Any(t => t.IsCashDividend);
        RootBorder.Visibility = anyDivTradesEver ? Visibility.Visible : Visibility.Collapsed;
        if (!anyDivTradesEver)
            return;

        for (var m = 1; m <= 12; m++)
        {
            var total = monthlyTotals.TryGetValue(m, out var v) ? v : 0m;
            var label = new DateTime(year, m, 1).ToString("MMM", CultureInfo.CurrentCulture);
            var hasData = total > 0;

            var btn = new Button
            {
                Cursor = hasData ? Cursors.Hand : Cursors.Arrow,
                IsEnabled = hasData,
                Style = BuildMonthCellStyle(hasData),
                Tag = m, // store month for click handler
            };

            var stack = new StackPanel { Margin = new Thickness(8, 6, 8, 6) };

            // Month label
            stack.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource(hasData ? "AppTextPrimary" : "AppTextMuted"),
            });

            if (hasData)
            {
                // Green dot + amount
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
                row.Children.Add(new Ellipse
                {
                    Width = 6,
                    Height = 6,
                    Fill = (Brush)FindResource("AppUp"),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 6, 0),
                });
                row.Children.Add(new TextBlock
                {
                    Text = total.ToString("N0"),
                    FontFamily = (FontFamily)FindResource("FontTabular"),
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (Brush)FindResource("AppUp"),
                });
                stack.Children.Add(row);

                // Click → filter trades to this month's dividends
                var month = m;
                btn.Click += (_, _) =>
                {
                    if (DataContext is PortfolioViewModel pvm)
                        pvm.FilterByDividendMonthCommand.Execute(month);
                };
            }

            btn.Content = stack;
            MonthGrid.Children.Add(btn);
        }
    }

    private Style BuildMonthCellStyle(bool hasData)
    {
        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(BackgroundProperty, FindResource("AppBackground")));
        style.Setters.Add(new Setter(BorderBrushProperty, FindResource(hasData ? "AppBorder" : "AppBorderLight")));
        style.Setters.Add(new Setter(BorderThicknessProperty, new Thickness(1)));
        style.Setters.Add(new Setter(MarginProperty, new Thickness(2)));
        style.Setters.Add(new Setter(HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        style.Setters.Add(new Setter(FocusVisualStyleProperty, null));

        // Template with hover effect
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border), "Bd");
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(BackgroundProperty));
        border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(BorderBrushProperty));
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));

        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        border.AppendChild(content);
        template.VisualTree = border;

        if (hasData)
        {
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BackgroundProperty,
                new DynamicResourceExtension("AppHover"), "Bd"));
            hover.Setters.Add(new Setter(Border.BorderBrushProperty,
                new DynamicResourceExtension("AppAccent"), "Bd"));
            template.Triggers.Add(hover);
        }

        style.Setters.Add(new Setter(TemplateProperty, template));

        if (!hasData)
            style.Setters.Add(new Setter(OpacityProperty, 0.5));

        style.Seal();
        return style;
    }
}
