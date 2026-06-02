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

        // 變更若來自 undo/redo，就不要再重新格式化，否則會把使用者剛還原的內容又格式化回去，
        // 形成「Ctrl+Z 沒反應」的迴圈。讓原生 undo/redo 的結果原樣保留。
        if (e.UndoAction is UndoAction.Undo or UndoAction.Redo)
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

            var caretIndex = Math.Clamp(tb.CaretIndex, 0, text.Length);

            var formatted = Format(text);
            if (formatted == text)
                return;

            var newCaret = ComputeCaretIndex(text, caretIndex, formatted);
            tb.Text = formatted;
            tb.CaretIndex = newCaret;
        }
        finally
        {
            _isFormatting = false;
        }
    }

    /// <summary>
    /// 把原字串的游標位置映射到格式化後的字串。rawCaret = 原字串去掉逗號後、caret 之前的字元數。
    /// Format 可能去掉前導 0（significant 字元變少，且都在最前面）；若被去掉的字元落在 caret 之前，
    /// rawCaret 會多算導致游標往右跳，因此先扣掉「caret 之前被去掉的字元數」再映射。
    /// </summary>
    internal static int ComputeCaretIndex(string original, int caretIndex, string formatted)
    {
        var clamped = Math.Clamp(caretIndex, 0, original.Length);
        var rawCaret = original[..clamped].Replace(",", "").Length;

        var removed = original.Replace(",", "").Length - formatted.Replace(",", "").Length;
        if (removed > 0)
            rawCaret -= Math.Min(rawCaret, removed);

        var newCaret = 0;
        var counted = 0;
        while (newCaret < formatted.Length && counted < rawCaret)
        {
            if (formatted[newCaret] != ',')
                counted++;
            newCaret++;
        }

        return Math.Min(newCaret, formatted.Length);
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
