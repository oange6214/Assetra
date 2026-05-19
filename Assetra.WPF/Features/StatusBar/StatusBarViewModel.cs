using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Windows.Media;
using Assetra.Core.Interfaces;
using Assetra.Core.Interfaces.Sync;
using Assetra.Core.Models.Sync;
using Assetra.WPF.Infrastructure;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Assetra.WPF.Features.StatusBar;

/// <summary>
/// Owns the bottom status bar for Assetra. Two indicators side-by-side:
/// <list type="number">
///   <item>Sync state — colored dot + label (Disabled/Idle/Pending/Syncing/Failed).
///     Drives the multi-device awareness story per
///     <c>docs/planning/Sync-Status-Indicator.md</c>.</item>
///   <item>Market open/closed — clock-driven TWSE indicator (09:00–13:30 weekdays).
///     Pure local clock; no real-time feed.</item>
/// </list>
/// Plus the wall clock on the far right (HH:mm:ss, ticks every second).
/// </summary>
public partial class StatusBarViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan MarketOpen = new(9, 0, 0);
    private static readonly TimeSpan MarketClose = new(13, 30, 0);

    private readonly CompositeDisposable _disposables = new();
    private readonly ILocalizationService _localization;
    private readonly IGlobalSyncStatusService? _sync;

    [ObservableProperty] private bool _isMarketOpen;
    [ObservableProperty] private string _marketStatusText = string.Empty;
    [ObservableProperty] private string _clockText = string.Empty;

    // ── Sync indicator ──────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SyncStatusText))]
    [NotifyPropertyChangedFor(nameof(SyncStatusBrush))]
    private GlobalSyncSnapshot _syncSnapshot = new(GlobalSyncState.Disabled, 0, null, null);

    public string SyncStatusText
    {
        get
        {
            return _syncSnapshot.State switch
            {
                GlobalSyncState.Disabled => _localization.Get("StatusBar.Sync.Disabled", "未啟用同步"),
                GlobalSyncState.Idle     => _localization.Get("StatusBar.Sync.Synced", "已同步"),
                GlobalSyncState.Syncing  => _localization.Get("StatusBar.Sync.Syncing", "同步中…"),
                GlobalSyncState.Failed   => _localization.Get("StatusBar.Sync.Failed", "同步失敗"),
                GlobalSyncState.Offline  => _localization.Get("StatusBar.Sync.Offline", "離線"),
                GlobalSyncState.Pending  => string.Format(
                    _localization.Get("StatusBar.Sync.PendingFormat", "{0} 筆待同步"),
                    _syncSnapshot.TotalPending),
                _ => string.Empty,
            };
        }
    }

    public Brush SyncStatusBrush => _syncSnapshot.State switch
    {
        GlobalSyncState.Idle     => Freeze(new SolidColorBrush(Color.FromRgb(34, 197, 94))),  // green-500
        GlobalSyncState.Pending  => Freeze(new SolidColorBrush(Color.FromRgb(249, 115, 22))), // orange-500
        GlobalSyncState.Syncing  => Freeze(new SolidColorBrush(Color.FromRgb(59, 130, 246))), // blue-500
        GlobalSyncState.Failed   => Freeze(new SolidColorBrush(Color.FromRgb(239, 68, 68))),  // red-500
        _                        => Freeze(new SolidColorBrush(Color.FromRgb(148, 163, 184))), // slate-400
    };

    private static Brush Freeze(SolidColorBrush b) { b.Freeze(); return b; }

    public StatusBarViewModel(
        IScheduler uiScheduler,
        ILocalizationService localization,
        IGlobalSyncStatusService? sync = null)
    {
        ArgumentNullException.ThrowIfNull(uiScheduler);
        ArgumentNullException.ThrowIfNull(localization);

        _localization = localization;
        _sync = sync;

        UpdateStatus(DateTime.Now);

        if (_sync is not null)
        {
            SyncSnapshot = _sync.Current;
            _sync.Changed += OnSyncChanged;
        }

        localization.LanguageChanged += OnLanguageChanged;

        // Tick every second to refresh clock + market status. Sync indicator
        // is event-driven, not tick-driven, so it lives outside this loop.
        Observable.Interval(TimeSpan.FromSeconds(1), uiScheduler)
            .Subscribe(_ => UpdateStatus(DateTime.Now))
            .DisposeWith(_disposables);
    }

    private void OnSyncChanged(object? sender, GlobalSyncSnapshot snapshot)
    {
        // Sync service may emit on a non-UI thread; marshal via the property
        // setter on the dispatcher.
        if (System.Windows.Application.Current?.Dispatcher.CheckAccess() == false)
        {
            System.Windows.Application.Current.Dispatcher.BeginInvoke(() => SyncSnapshot = snapshot);
            return;
        }
        SyncSnapshot = snapshot;
    }

    private void UpdateStatus(DateTime now)
    {
        ClockText = now.ToString("HH:mm:ss");
        IsMarketOpen = IsTwseOpen(now);
        MarketStatusText = IsMarketOpen
            ? _localization.Get("StatusBar.MarketOpen", "開盤中")
            : _localization.Get("StatusBar.MarketClosed", "休市");
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        UpdateStatus(DateTime.Now);
        // Re-render the sync label in the new language without changing the
        // underlying state. Property change notifications fire via attribute.
        OnPropertyChanged(nameof(SyncStatusText));
    }

    private static bool IsTwseOpen(DateTime now)
    {
        if (now.DayOfWeek == DayOfWeek.Saturday || now.DayOfWeek == DayOfWeek.Sunday)
            return false;
        var tod = now.TimeOfDay;
        return tod >= MarketOpen && tod <= MarketClose;
    }

    public void Dispose()
    {
        _localization.LanguageChanged -= OnLanguageChanged;
        if (_sync is not null) _sync.Changed -= OnSyncChanged;
        _disposables.Dispose();
    }
}
