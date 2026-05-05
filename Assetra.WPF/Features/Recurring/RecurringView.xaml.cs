using System.Windows;
using System.Windows.Controls;

namespace Assetra.WPF.Features.Recurring;

public partial class RecurringView : UserControl
{
    public RecurringView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        DataContextChanged += OnDataContextChanged;
        IsVisibleChanged += OnIsVisibleChanged;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await RequestLoadIfReadyAsync();
    }

    private async void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        await RequestLoadIfReadyAsync();
    }

    private async void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
            await RequestLoadIfReadyAsync();
    }

    private async Task RequestLoadIfReadyAsync()
    {
        if (IsVisible && DataContext is RecurringViewModel vm && !vm.IsLoaded)
            await vm.LoadAsync();
    }
}
