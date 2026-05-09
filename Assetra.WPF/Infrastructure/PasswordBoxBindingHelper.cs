using System.Windows;
using System.Windows.Controls;

namespace Assetra.WPF.Infrastructure;

/// <summary>
/// Attached property that lets <see cref="PasswordBox"/> bind to a string
/// without exposing the password in plaintext on the visual tree. WPF's
/// <c>PasswordBox.Password</c> is intentionally not a DependencyProperty for
/// security; this helper bridges Mode=TwoWay binding without breaking that
/// principle.
///
/// <para>
/// Usage:
/// <code>
/// &lt;PasswordBox infra:PasswordBoxBindingHelper.BoundPassword=
///     "{Binding LlmApiKey, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" /&gt;
/// </code>
/// </para>
/// </summary>
public static class PasswordBoxBindingHelper
{
    public static readonly DependencyProperty BoundPasswordProperty =
        DependencyProperty.RegisterAttached(
            "BoundPassword",
            typeof(string),
            typeof(PasswordBoxBindingHelper),
            new FrameworkPropertyMetadata(
                string.Empty,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnBoundPasswordChanged));

    private static readonly DependencyProperty UpdatingProperty =
        DependencyProperty.RegisterAttached(
            "Updating", typeof(bool), typeof(PasswordBoxBindingHelper), new PropertyMetadata(false));

    public static string GetBoundPassword(DependencyObject d) => (string)d.GetValue(BoundPasswordProperty);
    public static void SetBoundPassword(DependencyObject d, string value) => d.SetValue(BoundPasswordProperty, value);

    private static bool GetUpdating(DependencyObject d) => (bool)d.GetValue(UpdatingProperty);
    private static void SetUpdating(DependencyObject d, bool v) => d.SetValue(UpdatingProperty, v);

    private static void OnBoundPasswordChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not PasswordBox pb) return;

        // Hook the PasswordChanged event exactly once.
        pb.PasswordChanged -= HandlePasswordChanged;
        pb.PasswordChanged += HandlePasswordChanged;

        if (!GetUpdating(pb))
        {
            // Push from VM → control without echoing back.
            pb.Password = (string)e.NewValue ?? string.Empty;
        }
    }

    private static void HandlePasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not PasswordBox pb) return;
        SetUpdating(pb, true);
        try
        {
            SetBoundPassword(pb, pb.Password);
        }
        finally
        {
            SetUpdating(pb, false);
        }
    }
}
