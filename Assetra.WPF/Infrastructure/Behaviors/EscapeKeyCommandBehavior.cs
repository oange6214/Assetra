using System.Windows;
using System.Windows.Input;

namespace Assetra.WPF.Infrastructure.Behaviors;

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
        if (d is not UIElement element)
            return;

        element.PreviewKeyDown -= OnPreviewKeyDown;
        if (e.NewValue is ICommand)
            element.PreviewKeyDown += OnPreviewKeyDown;
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
