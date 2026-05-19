namespace Assetra.Core.Interfaces.Sync;

/// <summary>
/// Signals emitted by the background sync runner around each push/pull tick.
/// Consumed by <see cref="IGlobalSyncStatusService"/> to drive the state machine
/// (Idle ↔ Syncing ↔ Failed) and to decrement the pending counter when a batch
/// of changes is successfully pushed.
/// </summary>
public interface IBackgroundSyncSignals
{
    /// <summary>Fired when a sync tick begins (any direction).</summary>
    event EventHandler? SyncStarted;

    /// <summary>
    /// Fired when a sync tick finishes successfully. Argument is the total number
    /// of envelopes pushed in this tick across all domains, so the status service
    /// can decrement the aggregate counter without rescanning DB.
    /// </summary>
    event EventHandler<int>? SyncCompleted;

    /// <summary>
    /// Fired when a sync tick throws. Argument carries the short user-facing
    /// message (e.g. "網路逾時"). Counter is preserved — failed pushes stay in
    /// the queue for the next tick.
    /// </summary>
    event EventHandler<string>? SyncFailed;

    /// <summary>
    /// Fired when the sync passphrase is set or cleared. true = sync enabled.
    /// Drives Disabled ↔ Idle transitions.
    /// </summary>
    event EventHandler<bool>? EnabledChanged;
}
