using System.Collections.ObjectModel;
using System.Windows.Threading;
using Assetra.WPF.Infrastructure;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Assetra.WPF.Features.Snackbar;

public sealed partial class SnackbarViewModel : ObservableObject
{
    private const int MaxItems = 5;
    private static readonly TimeSpan DefaultDuration = TimeSpan.FromSeconds(4);
    private readonly HashSet<SnackbarItemViewModel> _autoDismissStarted = [];

    private readonly ObservableCollection<SnackbarItemViewModel> _items = [];
    public ReadOnlyObservableCollection<SnackbarItemViewModel> Items { get; }

    public SnackbarViewModel()
    {
        Items = new ReadOnlyObservableCollection<SnackbarItemViewModel>(_items);
    }

    public void Show(string message, SnackbarKind kind)
        => ShowCore(message, kind, null, null);

    public void Show(string message, string actionLabel, Action onAction, SnackbarKind kind)
        => ShowCore(message, kind, actionLabel, onAction);

    private void ShowCore(string message, SnackbarKind kind, string? actionLabel, Action? onAction)
    {
        // Remove oldest if at capacity
        if (_items.Count >= MaxItems)
            _items.RemoveAt(0);

        var item = new SnackbarItemViewModel(message, kind, actionLabel, onAction)
        {
            OnDismiss = Remove,
        };
        _items.Add(item);
    }

    public void StartAutoDismiss(SnackbarItemViewModel item)
    {
        if (!_items.Contains(item) || !_autoDismissStarted.Add(item))
            return;

        var timer = new DispatcherTimer { Interval = DefaultDuration };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Remove(item);
        };
        timer.Start();
    }

    private void Remove(SnackbarItemViewModel item)
    {
        if (_items.Contains(item))
            _items.Remove(item);
        _autoDismissStarted.Remove(item);
    }
}
