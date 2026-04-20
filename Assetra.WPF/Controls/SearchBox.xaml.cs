using System.Windows;
using System.Windows.Controls;

namespace Assetra.WPF.Controls;

public partial class SearchBox : UserControl
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(
            nameof(Text),
            typeof(string),
            typeof(SearchBox),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public static readonly DependencyProperty PlaceholderProperty =
        DependencyProperty.Register(
            nameof(Placeholder),
            typeof(string),
            typeof(SearchBox),
            new PropertyMetadata(""));

    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    /// <summary>
    /// Optional content rendered at the right end of the filter bar, next to the search
    /// input (e.g. an "＋ 新增現金" ghost button). Sits inside the same styled bar so the
    /// search input and the action share one visual container.
    /// </summary>
    public static readonly DependencyProperty RightContentProperty =
        DependencyProperty.Register(
            nameof(RightContent),
            typeof(object),
            typeof(SearchBox),
            new PropertyMetadata(null));

    public object? RightContent
    {
        get => GetValue(RightContentProperty);
        set => SetValue(RightContentProperty, value);
    }

    /// <summary>
    /// True 表示此控制項被嵌入在既有的 filter bar / 工具列中，移除外層容器的背景、
    /// 上下分隔線與 padding，只保留純粹的搜尋輸入框樣式。預設 false 保留獨立 bar 樣式。
    /// </summary>
    public static readonly DependencyProperty IsInlineProperty =
        DependencyProperty.Register(
            nameof(IsInline),
            typeof(bool),
            typeof(SearchBox),
            new PropertyMetadata(false));

    public bool IsInline
    {
        get => (bool)GetValue(IsInlineProperty);
        set => SetValue(IsInlineProperty, value);
    }

    public SearchBox() => InitializeComponent();
}
