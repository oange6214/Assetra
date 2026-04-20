using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace Assetra.WPF.Infrastructure.Controls;

/// <summary>
/// Attached behavior that makes a WPF <see cref="ToolTip"/> continuously follow the mouse cursor
/// while the pointer is inside the target element.
///
/// Usage (XAML):
/// <code>
///   xmlns:controls="clr-namespace:Assetra.WPF.Infrastructure.Controls"
///   controls:ToolTipBehavior.FollowMouse="True"
/// </code>
/// The element must also have a <see cref="ToolTip"/> set (inline or via ToolTipService).
/// </summary>
public static class ToolTipBehavior
{
    public static readonly DependencyProperty FollowMouseProperty =
        DependencyProperty.RegisterAttached(
            "FollowMouse",
            typeof(bool),
            typeof(ToolTipBehavior),
            new PropertyMetadata(false, OnFollowMouseChanged));

    public static bool GetFollowMouse(DependencyObject obj) =>
        (bool)obj.GetValue(FollowMouseProperty);

    public static void SetFollowMouse(DependencyObject obj, bool value) =>
        obj.SetValue(FollowMouseProperty, value);

    private static void OnFollowMouseChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement fe)
            return;

        if ((bool)e.NewValue)
            fe.MouseMove += OnMouseMove;
        else
            fe.MouseMove -= OnMouseMove;
    }

    private static void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (sender is not FrameworkElement fe)
            return;

        // Resolve the ToolTip regardless of whether it was set via property or ToolTipService.
        var toolTip = fe.ToolTip as ToolTip
                   ?? ToolTipService.GetToolTip(fe) as ToolTip;

        if (toolTip is null)
            return;

        var pos = e.GetPosition(fe);

        toolTip.Placement       = PlacementMode.Relative;
        toolTip.PlacementTarget = fe;
        toolTip.HorizontalOffset = pos.X + 14;
        toolTip.VerticalOffset   = pos.Y + 14;
    }
}
