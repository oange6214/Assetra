using System.Windows;
using System.Windows.Controls;

namespace Assetra.WPF.Features.Fire;

public partial class FireView : UserControl
{
    public FireView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        IsVisibleChanged += OnIsVisibleChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => WarmGroupsIfReady();

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true) WarmGroupsIfReady();
    }

    // Portfolio-Groups-Refactor P6 — ensure the group ComboBox lights up first time the page shows.
    private void WarmGroupsIfReady()
    {
        if (IsVisible && DataContext is FireViewModel vm)
            _ = vm.EnsureGroupCatalogLoadedAsync();
    }
}
