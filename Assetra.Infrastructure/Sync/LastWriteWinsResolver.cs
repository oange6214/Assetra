using Assetra.Core.Interfaces.Sync;
using Assetra.Core.Models.Sync;

namespace Assetra.Infrastructure.Sync;

/// <summary>
/// 預設衝突策略：以 <see cref="EntityVersion.LastModifiedAt"/> 較新的一方為準。
/// 時間戳完全相同時，以 <see cref="EntityVersion.Version"/> 較大者勝；再相同時保留遠端
/// （deterministic：避免雙方各自堅持本端而陷入鎖死）。
/// </summary>
public sealed class LastWriteWinsResolver : IConflictResolver
{
    public SyncResolution Resolve(SyncConflict conflict)
    {
        ArgumentNullException.ThrowIfNull(conflict);

        var local = conflict.Local.Version;
        var remote = conflict.Remote.Version;

        if (local.LastModifiedAt > remote.LastModifiedAt)
            return SyncResolution.KeepLocal;
        if (local.LastModifiedAt < remote.LastModifiedAt)
            return SyncResolution.KeepRemote;

        if (local.Version > remote.Version)
            return SyncResolution.KeepLocal;

        return SyncResolution.KeepRemote;
    }
}
