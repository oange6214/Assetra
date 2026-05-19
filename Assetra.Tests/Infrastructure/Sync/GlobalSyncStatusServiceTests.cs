using System.Reactive.Concurrency;
using Assetra.Core.Interfaces.Sync;
using Assetra.Core.Models.Sync;
using Assetra.Infrastructure.Sync;
using Microsoft.Reactive.Testing;
using Xunit;

namespace Assetra.Tests.Infrastructure.Sync;

/// <summary>
/// Phase 1 — verifies the state machine and counter aggregation of
/// <see cref="GlobalSyncStatusService"/>. Uses a deterministic
/// <see cref="TestScheduler"/> so timer-driven polls happen on demand.
/// </summary>
public sealed class GlobalSyncStatusServiceTests : IDisposable
{
    private readonly FakeSignals _signals = new();
    private readonly FakeCounter _counter = new();
    private readonly TestScheduler _scheduler = new();
    private GlobalSyncStatusService? _svc;

    private GlobalSyncStatusService Build(bool enabled = true) =>
        _svc = new GlobalSyncStatusService(_signals, _counter, _scheduler,
            initiallyEnabled: enabled, pollInterval: TimeSpan.FromSeconds(5));

    public void Dispose() => _svc?.Dispose();

    [Fact]
    public void InitialState_Disabled_WhenNotEnabled()
    {
        var svc = Build(enabled: false);
        Assert.Equal(GlobalSyncState.Disabled, svc.Current.State);
        Assert.Equal(0, svc.Current.TotalPending);
    }

    [Fact]
    public void InitialState_Idle_WhenEnabledAndCounterZero()
    {
        var svc = Build(enabled: true);
        Assert.Equal(GlobalSyncState.Idle, svc.Current.State);
    }

    [Fact]
    public async Task RefreshAsync_AggregatesAcrossDomains()
    {
        _counter.SetCounts(new() { ["Trade"] = 2, ["Asset"] = 3, ["Category"] = 0 });
        var svc = Build(enabled: true);

        await svc.RefreshAsync();

        Assert.Equal(GlobalSyncState.Pending, svc.Current.State);
        Assert.Equal(5, svc.Current.TotalPending);
    }

    [Fact]
    public async Task SyncStarted_TransitionsToSyncing()
    {
        var svc = Build(enabled: true);
        GlobalSyncSnapshot? observed = null;
        svc.Changed += (_, s) => observed = s;

        _signals.RaiseStarted();

        Assert.NotNull(observed);
        Assert.Equal(GlobalSyncState.Syncing, observed!.State);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task SyncCompleted_RepollsCounterAndReturnsToIdle()
    {
        _counter.SetCounts(new() { ["Trade"] = 3 });
        var svc = Build(enabled: true);
        await svc.RefreshAsync();
        Assert.Equal(3, svc.Current.TotalPending);

        // After push, counter drops to 0 — simulate by mutating the fake
        // BEFORE raising the event so the immediate re-poll picks it up.
        _counter.SetCounts(new() { ["Trade"] = 0 });
        _signals.RaiseStarted();
        _signals.RaiseCompleted(pushed: 3);

        // Re-poll is fire-and-forget Task; await it via a settling helper.
        await WaitForAsync(() => svc.Current.TotalPending == 0);

        Assert.Equal(0, svc.Current.TotalPending);
        Assert.Equal(GlobalSyncState.Idle, svc.Current.State);
        Assert.NotNull(svc.Current.LastSyncedAt);
        Assert.Null(svc.Current.LastError);
    }

    [Fact]
    public void SyncFailed_TransitionsToFailedAndPreservesError()
    {
        var svc = Build(enabled: true);
        _signals.RaiseFailed("network timeout");
        Assert.Equal(GlobalSyncState.Failed, svc.Current.State);
        Assert.Equal("network timeout", svc.Current.LastError);
    }

    [Fact]
    public async Task SyncCompleted_ClearsErrorOnSuccess()
    {
        var svc = Build(enabled: true);
        _signals.RaiseFailed("oops");
        Assert.Equal(GlobalSyncState.Failed, svc.Current.State);

        _counter.SetCounts(new() { ["Trade"] = 0 });
        _signals.RaiseCompleted(pushed: 0);
        await WaitForAsync(() => svc.Current.LastError is null);

        Assert.Null(svc.Current.LastError);
        Assert.Equal(GlobalSyncState.Idle, svc.Current.State);
    }

    [Fact]
    public void EnabledChanged_True_TransitionsFromDisabledToIdle()
    {
        var svc = Build(enabled: false);
        Assert.Equal(GlobalSyncState.Disabled, svc.Current.State);

        _signals.RaiseEnabledChanged(true);

        Assert.Equal(GlobalSyncState.Idle, svc.Current.State);
    }

    // ── Phase 2: GetPerDomain ────────────────────────────────────────

    [Fact]
    public async Task GetPerDomain_ReturnsRowsAfterRefresh()
    {
        _counter.SetCounts(new() { ["Trade"] = 2, ["Asset"] = 0, ["Portfolio"] = 5 });
        var svc = Build(enabled: true);

        await svc.RefreshAsync();

        var rows = svc.GetPerDomain();
        Assert.Equal(3, rows.Count);
        var trade = rows.Single(r => r.DomainKey == "Trade");
        var asset = rows.Single(r => r.DomainKey == "Asset");
        var portfolio = rows.Single(r => r.DomainKey == "Portfolio");
        Assert.Equal(2, trade.PendingCount);
        Assert.False(trade.IsSynced);
        Assert.Equal(0, asset.PendingCount);
        Assert.True(asset.IsSynced);
        Assert.Equal(5, portfolio.PendingCount);
    }

    [Fact]
    public async Task GetPerDomain_UpdatesOnSubsequentPolls()
    {
        _counter.SetCounts(new() { ["Trade"] = 1 });
        var svc = Build(enabled: true);
        await svc.RefreshAsync();
        Assert.Equal(1, svc.GetPerDomain().Single().PendingCount);

        _counter.SetCounts(new() { ["Trade"] = 4, ["Asset"] = 2 });
        await svc.RefreshAsync();

        var rows = svc.GetPerDomain();
        Assert.Equal(2, rows.Count);
        Assert.Equal(4, rows.Single(r => r.DomainKey == "Trade").PendingCount);
        Assert.Equal(2, rows.Single(r => r.DomainKey == "Asset").PendingCount);
    }

    [Fact]
    public async Task EnabledChanged_False_TransitionsToDisabledOverridingPending()
    {
        _counter.SetCounts(new() { ["Trade"] = 4 });
        var svc = Build(enabled: true);
        await svc.RefreshAsync();
        Assert.Equal(GlobalSyncState.Pending, svc.Current.State);

        _signals.RaiseEnabledChanged(false);

        Assert.Equal(GlobalSyncState.Disabled, svc.Current.State);
        // Counter preserved even when disabled — re-enabling brings Pending back.
        Assert.Equal(4, svc.Current.TotalPending);
    }

    private static async Task WaitForAsync(Func<bool> predicate, int timeoutMs = 1000)
    {
        var start = Environment.TickCount;
        while (!predicate())
        {
            if (Environment.TickCount - start > timeoutMs)
                throw new TimeoutException("Condition not met in time");
            await Task.Delay(10);
        }
    }

    // ── Test doubles ────────────────────────────────────────────────────
    private sealed class FakeSignals : IBackgroundSyncSignals
    {
        public event EventHandler? SyncStarted;
        public event EventHandler<int>? SyncCompleted;
        public event EventHandler<string>? SyncFailed;
        public event EventHandler<bool>? EnabledChanged;

        public void RaiseStarted() => SyncStarted?.Invoke(this, EventArgs.Empty);
        public void RaiseCompleted(int pushed) => SyncCompleted?.Invoke(this, pushed);
        public void RaiseFailed(string msg) => SyncFailed?.Invoke(this, msg);
        public void RaiseEnabledChanged(bool enabled) => EnabledChanged?.Invoke(this, enabled);
    }

    private sealed class FakeCounter : IPendingPushCounter
    {
        private Dictionary<string, int> _counts = new();
        public void SetCounts(Dictionary<string, int> counts) => _counts = counts;
        public Task<IReadOnlyDictionary<string, int>> CountPendingByDomainAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<string, int>>(_counts);
    }
}
