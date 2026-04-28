using Assetra.Core.Models.Sync;

namespace Assetra.Core.Interfaces.Sync;

/// <summary>
/// 標記 entity 參與雲端同步：暴露 <see cref="EntityVersion"/> 欄位即可。
/// <para>
/// v0.20.0 不強制既有 entity 實作此介面（避免大規模 schema migration）；
/// 後續 sprint 在新 entity 與選定的既有 entity 上逐步啟用。
/// </para>
/// </summary>
public interface IVersionedEntity
{
    Guid EntityId { get; }
    EntityVersion Version { get; }
}
