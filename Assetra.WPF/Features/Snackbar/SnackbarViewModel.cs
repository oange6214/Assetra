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

    public ObservableCollection<SnackbarItemViewModel> Items { get; } = [];

    public void Show(string message, SnackbarKind kind)
    {
        // Remove oldest if at capacity
        if (Items.Count >= MaxItems)
            Items.RemoveAt(0);

        var item = new SnackbarItemViewModel(message, kind)
        {
            OnDismiss = Remove
        };
        Items.Add(item);
    }

    public void StartAutoDismiss(SnackbarItemViewModel item)
    {
        if (!Items.Contains(item) || !_autoDismissStarted.Add(item))
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
        if (Items.Contains(item))
            Items.Remove(item);
        _autoDismissStarted.Remove(item);
    }
}
