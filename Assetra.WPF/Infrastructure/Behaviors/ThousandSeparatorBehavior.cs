using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Assetra.WPF.Infrastructure.Behaviors;

/// <summary>
/// Attached behavior that formats a TextBox value with thousand separators as the user types.
/// Strips existing commas before reformatting and preserves the logical caret position.
/// Compatible with ParseHelpers.TryParseDecimal (NumberStyles.Any includes AllowThousands).
/// </summary>
public static class ThousandSeparatorBehavior
{
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(ThousandSeparatorBehavior),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject d) => (bool)d.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject d, bool value) => d.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox tb)
            return;
        if ((bool)e.NewValue)
        {
            tb.TextChanged -= OnTextChanged;
            tb.Loaded -= OnLoaded;
            tb.TextChanged += OnTextChanged;
            tb.Loaded += OnLoaded;

            if (tb.ReadLocalValue(Control.HorizontalContentAlignmentProperty) == DependencyProperty.UnsetValue)
                tb.HorizontalContentAlignment = HorizontalAlignment.Right;

            // P4.9d — HorizontalContentAlignment 只控制 placeholder TextBlock 的對齊；
            // 真正影響 text + caret 對齊的是 TextAlignment（直接 property on TextBox）。
            // 沒設 TextAlignment 時 caret 在 empty 狀態跑左邊，跟右靠 placeholder 衝突。
            if (tb.ReadLocalValue(TextBox.TextAlignmentProperty) == DependencyProperty.UnsetValue)
                tb.TextAlignment = TextAlignment.Right;

            if (tb.IsLoaded)
                _ = tb.Dispatcher.BeginInvoke(() => FormatTextBox(tb), DispatcherPriority.Loaded);
        }
        else
        {
            tb.TextChanged -= OnTextChanged;
            tb.Loaded -= OnLoaded;
        }
    }

    // ThreadStatic so re-entrancy is blocked per UI thread without a shared flag.
    [ThreadStatic]
    private static bool _isFormatting;

    private static void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isFormatting || sender is not TextBox tb)
            return;
        FormatTextBox(tb);
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_isFormatting || sender is not TextBox tb)
            return;
        FormatTextBox(tb);
    }

    private static void FormatTextBox(TextBox tb)
    {
        _isFormatting = true;
        try
        {
            var text = tb.Text;
            if (string.IsNullOrEmpty(text))
                return;

            // Raw caret = position ignoring commas already in the text.
            var caretIndex = Math.Clamp(tb.CaretIndex, 0, text.Length);
            var rawCaret = text[..caretIndex].Replace(",", "").Length;

            var formatted = Format(text);
            if (formatted == text)
                return;

            tb.Text = formatted;

            // Walk the formatted string and count non-comma chars until rawCaret reached.
            var newCaret = 0;
            var counted = 0;
            while (newCaret < formatted.Length && counted < rawCaret)
            {
                if (formatted[newCaret] != ',')
                    counted++;
                newCaret++;
            }
            tb.CaretIndex = Math.Min(newCaret, formatted.Length);
        }
        finally
        {
            _isFormatting = false;
        }
    }

    private static string Format(string text)
    {
        var negative = text.StartsWith('-');
        var body = negative ? text[1..] : text;

        // Strip anything that isn't a digit, decimal point, or comma (e.g. "=", letters).
        body = new string(body.Where(c => char.IsDigit(c) || c == '.' || c == ',').ToArray());
        body = body.Replace(",", "");

        var dotIdx = body.IndexOf('.');
        var intPart = dotIdx >= 0 ? body[..dotIdx] : body;
        var fracPart = dotIdx >= 0 ? body[dotIdx..] : string.Empty;

        // 去掉多餘前導 0（"00123" → "123"；"000" → "0"），避免 textbox 出現 "00,123,…"。
        // 整數位數 ≤ 1（"0"、單一數字、或小數情況的空字串）不動，保留 "0.5" / ".5"。
        if (intPart.Length > 1)
        {
            intPart = intPart.TrimStart('0');
            if (intPart.Length == 0)
                intPart = "0";
        }

        if (intPart.Length <= 3)
            return (negative ? "-" : "") + intPart + fracPart;

        var sb = new StringBuilder();
        var offset = intPart.Length % 3;
        if (offset > 0)
            sb.Append(intPart[..offset]);
        for (var i = offset; i < intPart.Length; i += 3)
        {
            if (i > 0)
                sb.Append(',');
            sb.Append(intPart, i, 3);
        }

        return (negative ? "-" : "") + sb + fracPart;
    }
}
