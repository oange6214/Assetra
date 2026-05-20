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

    /// <summary>
    /// P2.13 — 今日漲跌 % 文字（已含「今日」前綴），由 MainViewModel push 進來。
    /// 空字串表示沒資料（隱藏 chip）。Status bar 上市場狀態旁邊顯示 e.g.「市場已收盤 · 今日 +0.82%」。
    /// </summary>
    [ObservableProperty] private string _todayReturnText = string.Empty;

    /// <summary>True 當今日漲跌 ≥ 0 — 控制 chip 顏色 (Brush.Up vs Brush.Down)。</summary>
    [ObservableProperty] private bool _isTodayReturnPositive = true;

    // ── Sync indicator ──────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SyncStatusText))]
    [NotifyPropertyChangedFor(nameof(SyncStatusBrush))]
    private GlobalSyncSnapshot _syncSnapshot = new(GlobalSyncState.Disabled, 0, null, null);

    public string SyncStatusText
    {
        get
        {
            var baseLabel = _syncSnapshot.State switch
            {
                GlobalSyncState.Disabled => _localization.Get("StatusBar.Sync.Disabled", "同步未開啟"),
                GlobalSyncState.Idle     => _localization.Get("StatusBar.Sync.Synced", "已同步"),
                GlobalSyncState.Syncing  => _localization.Get("StatusBar.Sync.Syncing", "同步中…"),
                GlobalSyncState.Failed   => _localization.Get("StatusBar.Sync.Failed", "同步未成功"),
                GlobalSyncState.Offline  => _localization.Get("StatusBar.Sync.Offline", "離線模式"),
                GlobalSyncState.Pending  => string.Format(
                    _localization.Get("StatusBar.Sync.PendingFormat", "{0} 筆待同步"),
                    _syncSnapshot.TotalPending),
                _ => string.Empty,
            };

            // P2.12 — Rich data 組合：Idle/Pending 狀態下加上「上次同步 14:23」尾巴，
            // 讓使用者一眼知道資料新鮮度。Failed 加最後一次嘗試時間幫助 debug。
            // 其他狀態 (Disabled/Syncing/Offline) 維持簡短不加尾。
            if (_syncSnapshot.LastSyncedAt is { } lastSync &&
                _syncSnapshot.State is GlobalSyncState.Idle or GlobalSyncState.Pending or GlobalSyncState.Failed)
            {
                var local = lastSync.ToLocalTime();
                var format = _localization.Get("StatusBar.Sync.LastSyncedFormat", "{0} · 上次同步 {1:HH:mm}");
                return string.Format(format, baseLabel, local.DateTime);
            }

            return baseLabel;
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

    /// <summary>
    /// Sync-Status-Indicator Phase 2 popover. Lives on the status bar so the
    /// chip can toggle <c>SyncPopover.IsOpen</c> directly via binding.
    /// </summary>
    public SyncStatusPopoverViewModel? SyncPopover { get; }

    public StatusBarViewModel(
        IScheduler uiScheduler,
        ILocalizationService localization,
        IGlobalSyncStatusService? sync = null,
        SyncStatusPopoverViewModel? syncPopover = null)
    {
        ArgumentNullException.ThrowIfNull(uiScheduler);
        ArgumentNullException.ThrowIfNull(localization);

        _localization = localization;
        _sync = sync;
        SyncPopover = syncPopover;

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
        // yyyy-MM-dd HH:mm:ss — 多裝置情境下 user 常會想知道「現在這個 client
        // 顯示的是哪一天」（NAS / VM 上若時區跑掉，沒日期不易察覺）。
        ClockText = now.ToString("yyyy-MM-dd HH:mm:ss");
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
