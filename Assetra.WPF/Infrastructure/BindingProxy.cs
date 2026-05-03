using System.Windows;

namespace Assetra.WPF.Infrastructure;

/// <summary>
/// Pass DataContext into XAML islands that live outside the visual / logical
/// tree (DataGridColumn headers, ContextMenu items, ToolTip popups, etc.) by
/// declaring a proxy in a parent Resources block and binding through it.
/// <para>
/// Freezable participates in the inheritance context for DataContext, so a
/// proxy declared inside <c>DataGrid.Resources</c> with <c>Data="{Binding}"</c>
/// captures the DataGrid's DataContext and is then reachable via StaticResource
/// from any column header / cell template.
/// </para>
/// </summary>
public sealed class BindingProxy : Freezable
{
    public static readonly DependencyProperty DataProperty =
        DependencyProperty.Register(nameof(Data), typeof(object), typeof(BindingProxy));

    public object? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    protected override Freezable CreateInstanceCore() => new BindingProxy();
}
