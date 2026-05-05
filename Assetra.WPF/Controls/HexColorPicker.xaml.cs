using System.Collections;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace Assetra.WPF.Controls;

public partial class HexColorPicker : UserControl
{
    private static readonly Regex HexRegex = new(@"^#?[0-9A-Fa-f]{6}$", RegexOptions.Compiled);

    public static readonly DependencyProperty SelectedColorHexProperty =
        DependencyProperty.Register(
            nameof(SelectedColorHex),
            typeof(string),
            typeof(HexColorPicker),
            new FrameworkPropertyMetadata(
                string.Empty,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                null,
                (_, value) => NormalizeHexOrEmpty(value as string)));

    public string SelectedColorHex
    {
        get => (string)GetValue(SelectedColorHexProperty);
        set => SetValue(SelectedColorHexProperty, NormalizeHexOrEmpty(value));
    }

    public static readonly DependencyProperty PresetsProperty =
        DependencyProperty.Register(
            nameof(Presets),
            typeof(IEnumerable),
            typeof(HexColorPicker),
            new PropertyMetadata(null));

    public IEnumerable? Presets
    {
        get => (IEnumerable?)GetValue(PresetsProperty);
        set => SetValue(PresetsProperty, value);
    }

    public HexColorPicker()
    {
        InitializeComponent();
        PresetsItemsControl.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(nameof(Presets)) { Source = this });
    }

    private void OpenButton_OnClick(object sender, RoutedEventArgs e)
    {
        HexInput.Text = SelectedColorHex;
        HexError.Visibility = Visibility.Collapsed;
        PickerPopup.IsOpen = true;
        HexInput.Focus();
        HexInput.SelectAll();
    }

    private void Swatch_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string hex })
        {
            SelectedColorHex = hex;
            PickerPopup.IsOpen = false;
        }
    }

    private void ApplyHex_OnClick(object sender, RoutedEventArgs e) => CommitHex();

    private void HexInput_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitHex();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            PickerPopup.IsOpen = false;
            e.Handled = true;
        }
    }

    private void CommitHex()
    {
        var input = HexInput.Text?.Trim() ?? string.Empty;
        if (!HexRegex.IsMatch(input))
        {
            HexError.Visibility = Visibility.Visible;
            return;
        }

        SelectedColorHex = input;
        HexError.Visibility = Visibility.Collapsed;
        PickerPopup.IsOpen = false;
    }

    private static string NormalizeHexOrEmpty(string? value)
    {
        var input = value?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        return input.StartsWith("#", StringComparison.Ordinal)
            ? input.ToUpperInvariant()
            : "#" + input.ToUpperInvariant();
    }
}
