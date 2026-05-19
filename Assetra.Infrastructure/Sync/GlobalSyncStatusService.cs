using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Assetra.Core.Interfaces.Sync;
using Assetra.Core.Models.Sync;
// `DomainSyncStatus` lives in Assetra.Core.Models.Sync — imported above.

namespace Assetra.Infrastructure.Sync;

/// <summary>
/// Aggregates per-domain pending push counts + sync lifecycle events into a
/// single observable <see cref="GlobalSyncSnapshot"/> for the status bar.
///
/// <para>Data sources:
/// <list type="bullet">
///   <item><see cref="IPendingPushCounter"/> — polled every 5 seconds and once
///     immediately after each <see cref="IBackgroundSyncSignals.SyncCompleted"/>
///     event. Drives the <see cref="GlobalSyncSnapshot.TotalPending"/> count.</item>
///   <item><see cref="IBackgroundSyncSignals"/> — drives the state machine
///     (Idle ↔ Syncing ↔ Failed ↔ Disabled).</item>
/// </list>
/// </para>
///
/// <para>Concurrency: snapshot writes go through a <c>lock</c>. <c>Changed</c>
/// fires after the lock releases. UI subscribers should marshal to the
/// dispatcher themselves.</para>
/// </summary>
public sealed class GlobalSyncStatusService : IGlobalSyncStatusService
{
    private readonly IBackgroundSyncSignals _signals;
    private readonly IPendingPushCounter _counter;
    private readonly IScheduler _scheduler;
    private readonly CompositeDisposable _disposables = new();
    private readonly object _gate = new();

    private GlobalSyncState _state;
    private DateTimeOffset? _lastSyncedAt;
    private string? _lastError;
    private bool _enabled;
    private int _totalPending;
    private IReadOnlyList<DomainSyncStatus> _perDomain = Array.Empty<DomainSyncStatus>();
    private bool _disposed;

    public GlobalSyncStatusService(
        IBackgroundSyncSignals signals,
        IPendingPushCounter counter,
        IScheduler scheduler,
        bool initiallyEnabled = false,
        TimeSpan? pollInterval = null)
    {
        ArgumentNullException.ThrowIfNull(signals);
        ArgumentNullException.ThrowIfNull(counter);
        ArgumentNullException.ThrowIfNull(scheduler);

        _signals = signals;
        _counter = counter;
        _scheduler = scheduler;
        _enabled = initiallyEnabled;
        _state = initiallyEnabled ? GlobalSyncState.Idle : GlobalSyncState.Disabled;

        _signals.SyncStarted += OnSyncStarted;
        _signals.SyncCompleted += OnSyncCompleted;
        _signals.SyncFailed += OnSyncFailed;
        _signals.EnabledChanged += OnEnabledChanged;

        // 5-sec poll keeps counter ≤ 5 sec stale after a mutation. Counter cost
        // is sub-ms on WAL SQLite — running once per 5 sec is free.
        var pollSub = Observable.Interval(pollInterval ?? TimeSpan.FromSeconds(5), _scheduler)
            .Subscribe(tick => { var fireAndForget = PollAsync(); });
        _disposables.Add(pollSub);
    }

    public GlobalSyncSnapshot Current
    {
        get { lock (_gate) return BuildSnapshotLocked(); }
    }

    public event EventHandler<GlobalSyncSnapshot>? Changed;

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        await PollAsync(ct).ConfigureAwait(false);
    }

    private async Task PollAsync(CancellationToken ct = default)
    {
        try
        {
            var counts = await _counter.CountPendingByDomainAsync(ct).ConfigureAwait(false);
            var total = counts.Values.Sum();
            // Materialize per-domain snapshot in counter-defined order. Ordering
            // is stable across polls so the popover doesn't shuffle rows.
            var perDomain = counts
                .Select(kv => new DomainSyncStatus(kv.Key, kv.Value))
                .ToList();
            bool changed;
            lock (_gate)
            {
                changed = _totalPending != total
                          || !PerDomainEqualsLocked(perDomain);
                _totalPending = total;
                _perDomain = perDomain;
            }
            if (changed) Emit();
        }
        catch
        {
            // Best-effort poll; an isolated DB hiccup shouldn't crash the UI.
        }
    }

    private bool PerDomainEqualsLocked(IReadOnlyList<DomainSyncStatus> next)
    {
        if (_perDomain.Count != next.Count) return false;
        for (int i = 0; i < next.Count; i++)
        {
            if (_perDomain[i].DomainKey != next[i].DomainKey) return false;
            if (_perDomain[i].PendingCount != next[i].PendingCount) return false;
        }
        return true;
    }

    public IReadOnlyList<DomainSyncStatus> GetPerDomain()
    {
        lock (_gate) return _perDomain;
    }

    private void OnSyncStarted(object? sender, EventArgs e)
    {
        lock (_gate) { _state = GlobalSyncState.Syncing; }
        Emit();
    }

    private void OnSyncCompleted(object? sender, int pushed)
    {
        lock (_gate)
        {
            _lastSyncedAt = DateTimeOffset.UtcNow;
            _lastError = null;
            // Leave Syncing — settles to Idle/Pending in BuildSnapshotLocked
            // once the counter re-poll lands.
            _state = GlobalSyncState.Idle;
        }
        // Re-poll right away so the counter reflects the just-pushed batch.
        _ = PollAsync();
    }

    private void OnSyncFailed(object? sender, string message)
    {
        lock (_gate) { _lastError = message; }
        Emit();
    }

    private void OnEnabledChanged(object? sender, bool enabled)
    {
        lock (_gate) { _enabled = enabled; }
        Emit();
    }

    private void Emit()
    {
        GlobalSyncSnapshot snap;
        lock (_gate) { snap = BuildSnapshotLocked(); }
        Changed?.Invoke(this, snap);
    }

    private GlobalSyncSnapshot BuildSnapshotLocked()
    {
        var state = (_enabled, _lastError, _state, _totalPending) switch
        {
            (false, _, _, _)            => GlobalSyncState.Disabled,
            (true, not null, _, _)      => GlobalSyncState.Failed,
            (true, null, GlobalSyncState.Syncing, _) => GlobalSyncState.Syncing,
            (true, null, _, 0)          => GlobalSyncState.Idle,
            (true, null, _, _)          => GlobalSyncState.Pending,
        };
        return new GlobalSyncSnapshot(state, _totalPending, _lastSyncedAt, _lastError);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _signals.SyncStarted -= OnSyncStarted;
        _signals.SyncCompleted -= OnSyncCompleted;
        _signals.SyncFailed -= OnSyncFailed;
        _signals.EnabledChanged -= OnEnabledChanged;
        _disposables.Dispose();
    }
}
