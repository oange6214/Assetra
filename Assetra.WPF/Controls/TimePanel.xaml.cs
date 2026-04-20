using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Assetra.WPF.Controls;

/// <summary>
/// 時間選擇面板：每 15 分鐘一格，垂直捲動列表。
/// 選取的時段以 AppAccent 背景顯示。
/// </summary>
public partial class TimePanel : UserControl
{
    // Dependency Properties

    public static readonly DependencyProperty SelectedTimeProperty =
        DependencyProperty.Register(
            nameof(SelectedTime),
            typeof(TimeSpan),
            typeof(TimePanel),
            new FrameworkPropertyMetadata(
                TimeSpan.Zero,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnSelectedTimeChanged));

    public TimeSpan SelectedTime
    {
        get => (TimeSpan)GetValue(SelectedTimeProperty);
        set => SetValue(SelectedTimeProperty, value);
    }

    // Events

    public event EventHandler<TimeSpan>? TimePicked;

    // State

    private const int IntervalMinutes = 15;
    private static readonly int SlotCount = 24 * 60 / IntervalMinutes; // 96

    private Button? _selectedButton;

    // Constructor

    public TimePanel()
    {
        InitializeComponent();
        Loaded += (_, _) => BuildSlots();
    }

    // Build time slots

    private void BuildSlots()
    {
        TimeList.Children.Clear();
        _selectedButton = null;

        var selectedSnapped = SnapToSlot(SelectedTime);

        for (var i = 0; i < SlotCount; i++)
        {
            var ts = TimeSpan.FromMinutes(i * IntervalMinutes);
            var label = $"{ts.Hours:D2}:{ts.Minutes:D2}";
            var isSelected = ts == selectedSnapped;

            var btn = new Button
            {
                Content = new TextBlock
                {
                    Text = label,
                    FontFamily = (FontFamily)FindResource("FontTabular"),
                    FontSize = 13,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Foreground = isSelected
                        ? Brushes.White
                        : (Brush)FindResource("AppTextPrimary"),
                },
                Height = 32,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                Tag = ts,
                Style = BuildSlotStyle(isSelected),
            };

            btn.Click += Slot_Click;
            TimeList.Children.Add(btn);

            if (isSelected)
                _selectedButton = btn;
        }

        // 延遲捲動，讓選取項目出現在可視區域的中間
        if (_selectedButton is not null)
            Dispatcher.BeginInvoke(ScrollSelectedToCenter);
    }

    // Slot style builder

    private Style BuildSlotStyle(bool isSelected)
    {
        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(BackgroundProperty,
            isSelected ? FindResource("AppAccent") : Brushes.Transparent));
        style.Setters.Add(new Setter(BorderThicknessProperty, new Thickness(0)));
        style.Setters.Add(new Setter(CursorProperty, System.Windows.Input.Cursors.Hand));
        style.Setters.Add(new Setter(FocusVisualStyleProperty, null));
        style.Setters.Add(new Setter(HorizontalAlignmentProperty, HorizontalAlignment.Stretch));
        style.Setters.Add(new Setter(MarginProperty, new Thickness(2, 1, 2, 1)));
        style.Setters.Add(new Setter(TemplateProperty, BuildSlotTemplate()));
        style.Seal();
        return style;
    }

    private static ControlTemplate BuildSlotTemplate()
    {
        var template = new ControlTemplate(typeof(Button));

        var border = new FrameworkElementFactory(typeof(Border), "Bd");
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        border.SetBinding(Border.BackgroundProperty,
            new System.Windows.Data.Binding("Background")
            { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });

        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(content);

        template.VisualTree = border;

        // Hover trigger
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty,
            new DynamicResourceExtension("AppHover"), "Bd"));
        template.Triggers.Add(hover);

        return template;
    }

    // Snap to nearest 15-min slot

    private static TimeSpan SnapToSlot(TimeSpan ts)
    {
        var totalMinutes = (int)ts.TotalMinutes;
        var snapped = (totalMinutes / IntervalMinutes) * IntervalMinutes;
        return TimeSpan.FromMinutes(snapped);
    }

    // Events

    private void Slot_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is TimeSpan ts)
        {
            SelectedTime = ts;
            TimePicked?.Invoke(this, ts);
        }
    }

    private static void OnSelectedTimeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TimePanel panel && panel.IsLoaded)
            panel.BuildSlots();
    }

    // Public helpers

    /// <summary>捲動到目前選取的時間並重新 highlight。</summary>
    public void ScrollToSelected()
    {
        if (!IsLoaded)
            return;
        BuildSlots();
    }

    /// <summary>把選取的 Button 捲動到 ScrollViewer 可視區域的中央。</summary>
    private void ScrollSelectedToCenter()
    {
        if (_selectedButton is null)
            return;

        // 先確保 layout 已完成
        TimeScroller.UpdateLayout();

        var slotIndex = TimeList.Children.IndexOf(_selectedButton);
        if (slotIndex < 0)
            return;

        // 每格 32px height + 2px margin top + 2px margin bottom ≈ 實際偏移
        var itemOffset = slotIndex * _selectedButton.ActualHeight;
        var viewportHeight = TimeScroller.ViewportHeight;

        // 目標：把選取格放在視口中央
        var targetOffset = itemOffset - (viewportHeight / 2) + (_selectedButton.ActualHeight / 2);
        targetOffset = Math.Max(0, Math.Min(targetOffset, TimeScroller.ScrollableHeight));

        TimeScroller.ScrollToVerticalOffset(targetOffset);
    }
}
