namespace Assetra.Core.Models.Sync;

/// <summary>
/// Per-domain row surfaced in the Phase 2 popover. <see cref="DomainKey"/> is
/// the stable identifier the sync infrastructure uses (e.g. <c>"Trade"</c>),
/// which the UI maps to a localized label (e.g. 「交易記錄」) at render time.
/// </summary>
public sealed record DomainSyncStatus(
    string DomainKey,
    int PendingCount)
{
    /// <summary>Convenience flag — true when this domain has zero unsynced rows.</summary>
    public bool IsSynced => PendingCount == 0;
}
