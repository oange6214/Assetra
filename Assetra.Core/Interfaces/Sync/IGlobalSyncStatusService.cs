using Assetra.Core.Models.Sync;

namespace Assetra.Core.Interfaces.Sync;

/// <summary>
/// Aggregates per-domain sync state into a single observable snapshot for the
/// status bar (and a future popover with per-domain breakdown).
///
/// <para><b>Design invariant — must NOT poll the database.</b> Each sync repo
/// already raises a <see cref="ILocalChangeCountSource.LocalChangeCountChanged"/>
/// event on every mutation; <see cref="BackgroundSyncSignals"/> raises
/// SyncStarted / SyncCompleted / SyncFailed. The implementation seeds the
/// per-domain counter once on startup, then mutates it purely from events.
/// This keeps the indicator real-time without competing with
/// <c>BackgroundSyncService</c> for SQLite locks.</para>
/// </summary>
public interface IGlobalSyncStatusService : IDisposable
{
    /// <summary>Latest snapshot. Updated atomically before <see cref="Changed"/> fires.</summary>
    GlobalSyncSnapshot Current { get; }

    /// <summary>
    /// Raised whenever the snapshot changes (counter delta, sync started/ended/failed,
    /// passphrase enabled/disabled). UI binds once and re-renders on each fire.
    /// Always raised on the dispatcher thread the implementation was constructed on
    /// — implementers must marshal accordingly.
    /// </summary>
    event EventHandler<GlobalSyncSnapshot>? Changed;

    /// <summary>
    /// Re-seed the counter from the database. Called once at app startup; can be
    /// invoked again after a restore / wipe operation that bypasses the event flow.
    /// </summary>
    Task RefreshAsync(CancellationToken ct = default);
}
