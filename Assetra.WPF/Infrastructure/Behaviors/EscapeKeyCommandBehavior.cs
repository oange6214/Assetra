using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace Assetra.WPF.Infrastructure.Behaviors;

/// <summary>
/// 把 Esc 鍵綁到 modal close 命令的 attached behavior。
///
/// <para>關鍵設計：PreviewKeyDown 是 tunneling event，從 root 沿著
/// focus path 往下傳。如果 modal 開啟時鍵盤焦點還停在原本觸發的按鈕
/// （例如 NavRail 的「新增」按鈕），tunneling 路徑根本不會經過 modal
/// 的 Border，Esc 就毫無反應。</para>
///
/// <para>解法：監聽 element 的 <see cref="UIElement.IsVisibleChanged"/>，
/// 變 visible 時自動把鍵盤焦點推到該 element（通常是 modal 的 Border）。
/// Border 預設不可 focus，所以同時設 <see cref="UIElement.Focusable"/>。
/// 用 <see cref="DispatcherPriority.Input"/> 延遲 focus 避免跟 WPF 自己
/// 的 layout pass 搶 — 否則 element 還沒完成 measure 就 Focus 會被吃掉。</para>
/// </summary>
public static class EscapeKeyCommandBehavior
{
    public static readonly DependencyProperty CommandProperty =
        DependencyProperty.RegisterAttached(
            "Command",
            typeof(ICommand),
            typeof(EscapeKeyCommandBehavior),
            new PropertyMetadata(null, OnCommandChanged));

    public static ICommand? GetCommand(DependencyObject d) => (ICommand?)d.GetValue(CommandProperty);
    public static void SetCommand(DependencyObject d, ICommand? value) => d.SetValue(CommandProperty, value);

    private static void OnCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
            return;

        element.PreviewKeyDown -= OnPreviewKeyDown;
        element.IsVisibleChanged -= OnIsVisibleChanged;

        if (e.NewValue is ICommand)
        {
            element.PreviewKeyDown += OnPreviewKeyDown;
            element.IsVisibleChanged += OnIsVisibleChanged;

            // Border / Grid 等容器預設 Focusable=false。沒這行 Keyboard.Focus 會被忽略。
            element.Focusable = true;
        }
    }

    private static void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not FrameworkElement element || !element.IsVisible)
            return;

        // 延遲到 Input priority 才 Focus：layout pass 跑完、子控制項都生出來後再搶焦點，
        // 否則 WPF 的初始 focus logic 會在我們之後重新指派。
        element.Dispatcher.BeginInvoke(
            (Action)(() =>
            {
                if (!element.IsVisible) return;
                Keyboard.Focus(element);
            }),
            DispatcherPriority.Input);
    }

    private static void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || sender is not DependencyObject dependencyObject)
            return;

        var command = GetCommand(dependencyObject);
        if (command is null || !command.CanExecute(null))
            return;

        command.Execute(null);
        e.Handled = true;
    }
}
