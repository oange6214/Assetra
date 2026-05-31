using System.Windows;
using System.Windows.Controls;

namespace Assetra.WPF.Features.Fire;

public partial class FireView : UserControl
{
    private bool _isWarming;

    public FireView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        IsVisibleChanged += OnIsVisibleChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => WarmGroupsIfReady();

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
            WarmGroupsIfReady();
    }

    // Portfolio-Groups-Refactor P6 — ensure the group ComboBox lights up first time the page shows.
    private void WarmGroupsIfReady()
    {
        if (!IsVisible || DataContext is not FireViewModel vm || _isWarming)
            return;

        _ = WarmAsync(vm);
    }

    private async Task WarmAsync(FireViewModel vm)
    {
        _isWarming = true;
        try
        {
            await vm.EnsureGroupCatalogLoadedAsync().ConfigureAwait(true);
            await vm.LoadScenariosAsync().ConfigureAwait(true);
            await vm.LoadCurrentNetWorthAsync().ConfigureAwait(true);
        }
        finally
        {
            _isWarming = false;
        }
    }
}
