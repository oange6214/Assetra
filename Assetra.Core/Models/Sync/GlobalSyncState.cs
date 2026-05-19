namespace Assetra.Core.Models.Sync;

/// <summary>
/// High-level state machine for the multi-domain sync indicator.
/// Mapped to a colored dot + label in the status bar:
/// <list type="bullet">
///   <item><see cref="Disabled"/> — gray dot, "未啟用同步"</item>
///   <item><see cref="Idle"/> — green dot, "已同步"</item>
///   <item><see cref="Pending"/> — orange dot, "N 筆待同步"</item>
///   <item><see cref="Syncing"/> — animated spinner, "同步中…"</item>
///   <item><see cref="Failed"/> — red dot, "同步失敗"</item>
///   <item><see cref="Offline"/> — gray dot, "離線"</item>
/// </list>
/// </summary>
public enum GlobalSyncState
{
    /// <summary>User hasn't enabled cloud sync (no passphrase entered).</summary>
    Disabled = 0,
    /// <summary>All domains are fully synced — nothing in any pending queue.</summary>
    Idle = 1,
    /// <summary>One or more domains have unpushed changes but no push is in flight.</summary>
    Pending = 2,
    /// <summary>BackgroundSyncService is actively pushing or pulling right now.</summary>
    Syncing = 3,
    /// <summary>Last sync attempt threw. Counter preserved; user can retry.</summary>
    Failed = 4,
    /// <summary>Network unreachable (probed by a future heuristic; reserved).</summary>
    Offline = 5,
}
