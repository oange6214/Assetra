using Assetra.Core.Models.Sync;

namespace Assetra.Core.Interfaces.Sync;

/// <summary>
/// 持久化本裝置的 <see cref="SyncMetadata"/>（DeviceId / LastSyncAt / Cursor）。
/// 實作可選 AppSettings、獨立 keystore、或 SQLite 表，本介面不指定。
/// 同一裝置的 DeviceId 一旦產生不應變更。
/// </summary>
public interface ISyncMetadataStore
{
    Task<SyncMetadata> GetAsync(CancellationToken ct = default);
    Task SaveAsync(SyncMetadata metadata, CancellationToken ct = default);
}
