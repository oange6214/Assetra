using System.Windows;
using System.Windows.Input;

namespace Assetra.WPF.Infrastructure.Behaviors;

/// <summary>
/// Attached behavior that closes a modal overlay when the user clicks its backdrop.
/// Replaces three near-identical event handlers in <c>PortfolioView.xaml.cs</c>
/// (Cash / Liability / Position detail panels).
///
/// Usage in XAML:
/// <code>
/// &lt;Border Background="{DynamicResource AppModalOverlay}"
///         beh:BackdropClickToCloseBehavior.Command="{Binding CloseCashDetailCommand}" /&gt;
/// </code>
///
/// The handler ignores clicks that bubble up from inside child elements — only
/// clicks whose <see cref="MouseButtonEventArgs.OriginalSource"/> is the backdrop
/// itself trigger the command.
/// </summary>
public static class BackdropClickToCloseBehavior
{
    public static readonly DependencyProperty CommandProperty =
        DependencyProperty.RegisterAttached(
            "Command",
            typeof(ICommand),
            typeof(BackdropClickToCloseBehavior),
            new PropertyMetadata(null, OnCommandChanged));

    public static ICommand? GetCommand(DependencyObject d) =>
        (ICommand?)d.GetValue(CommandProperty);

    public static void SetCommand(DependencyObject d, ICommand? value) =>
        d.SetValue(CommandProperty, value);

    private static void OnCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement ui) return;
        ui.MouseLeftButtonDown -= OnMouseLeftButtonDown;
        if (e.NewValue is ICommand)
            ui.MouseLeftButtonDown += OnMouseLeftButtonDown;
    }

    private static void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not UIElement ui) return;
        if (!ReferenceEquals(e.OriginalSource, sender)) return;
        var cmd = GetCommand(ui);
        if (cmd is null || !cmd.CanExecute(null)) return;
        cmd.Execute(null);
        e.Handled = true;
    }
}
