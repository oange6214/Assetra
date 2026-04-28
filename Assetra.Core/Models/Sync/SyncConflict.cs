namespace Assetra.Core.Models.Sync;

/// <summary>
/// Pull / Push 期間偵測到的衝突：本端與遠端對同一 <see cref="SyncEnvelope.EntityId"/>
/// 都有後續修改。由 <see cref="Assetra.Core.Interfaces.Sync.IConflictResolver"/> 處理。
/// </summary>
public sealed record SyncConflict(SyncEnvelope Local, SyncEnvelope Remote)
{
    public Guid EntityId => Local.EntityId;
    public string EntityType => Local.EntityType;
}
