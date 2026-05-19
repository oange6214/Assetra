namespace Assetra.Core.Models.Sync;

/// <summary>
/// Snapshot of the multi-domain sync state surfaced to UI (status bar / popover).
/// Immutable record — emitted by <c>IGlobalSyncStatusService.Changed</c> on every
/// transition. UI subscribes once and re-renders on each emission.
/// </summary>
/// <param name="State">High-level state machine value the indicator binds to.</param>
/// <param name="TotalPending">
/// Sum of <c>is_pending_push = 1</c> rows across every sync store. 0 = fully synced.
/// </param>
/// <param name="LastSyncedAt">
/// UTC timestamp of the last successful push tick (any domain). null = never synced
/// in this process lifetime.
/// </param>
/// <param name="LastError">
/// Short message from the last <c>SyncFailed</c> event, or null if no error since
/// startup. Cleared when a subsequent push succeeds.
/// </param>
public sealed record GlobalSyncSnapshot(
    GlobalSyncState State,
    int TotalPending,
    DateTimeOffset? LastSyncedAt,
    string? LastError);
