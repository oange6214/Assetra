using Assetra.Core.Models.Sync;

namespace Assetra.Core.Interfaces.Sync;

/// <summary>
/// 把 <see cref="SyncConflict"/> 化為 <see cref="SyncResolution"/> 的策略物件。
/// 預設實作 <c>LastWriteWinsResolver</c>：以 <see cref="EntityVersion.LastModifiedAt"/> 較新者勝。
/// </summary>
public interface IConflictResolver
{
    SyncResolution Resolve(SyncConflict conflict);
}
