using System.Collections;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Assetra.WPF.Controls;

public partial class ColorPickerButton : UserControl
{
    private static readonly Regex HexRegex = new(@"^#?[0-9A-Fa-f]{6}$", RegexOptions.Compiled);

    public static readonly DependencyProperty SelectedColorProperty =
        DependencyProperty.Register(
            nameof(SelectedColor),
            typeof(string),
            typeof(ColorPickerButton),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public string SelectedColor
    {
        get => (string)GetValue(SelectedColorProperty);
        set => SetValue(SelectedColorProperty, value);
    }

    public static readonly DependencyProperty PresetsProperty =
        DependencyProperty.Register(
            nameof(Presets),
            typeof(IEnumerable),
            typeof(ColorPickerButton),
            new PropertyMetadata(null));

    public IEnumerable? Presets
    {
        get => (IEnumerable?)GetValue(PresetsProperty);
        set => SetValue(PresetsProperty, value);
    }

    public ColorPickerButton() => InitializeComponent();

    private void OpenButton_OnClick(object sender, RoutedEventArgs e)
    {
        HexInput.Text = SelectedColor;
        HexError.Visibility = Visibility.Collapsed;
        PickerPopup.IsOpen = true;
        HexInput.Focus();
        HexInput.SelectAll();
    }

    private void Swatch_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string hex })
        {
            SelectedColor = hex;
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

        SelectedColor = input.StartsWith('#') ? input.ToUpperInvariant() : "#" + input.ToUpperInvariant();
        HexError.Visibility = Visibility.Collapsed;
        PickerPopup.IsOpen = false;
    }
}
