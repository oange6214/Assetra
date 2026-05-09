using System.Collections.Specialized;
using System.Windows.Controls;

namespace Assetra.WPF.Features.Assistant;

public partial class AssistantView : UserControl
{
    public AssistantView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is AssistantViewModel vm)
        {
            // Hook auto-scroll on the underlying messages collection so any
            // new chat bubble drives the ScrollViewer to the bottom.
            ((INotifyCollectionChanged)vm.Messages).CollectionChanged += OnMessagesChanged;

            await vm.LoadHistoryAsync();
            await vm.LoadInsightsAsync();
            MessagesScrollViewer?.ScrollToEnd();
        }
    }

    private void OnUnloaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is AssistantViewModel vm)
            ((INotifyCollectionChanged)vm.Messages).CollectionChanged -= OnMessagesChanged;
    }

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add)
            MessagesScrollViewer?.ScrollToEnd();
    }
}
