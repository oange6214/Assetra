using Assetra.Core.Interfaces.Sync;
using Assetra.Core.Models.Sync;

namespace Assetra.Infrastructure.Sync;

/// <summary>
/// 純記憶體 <see cref="ISyncMetadataStore"/>。給測試 / 開發用，不持久化。
/// </summary>
public sealed class InMemorySyncMetadataStore : ISyncMetadataStore
{
    private readonly object _lock = new();
    private SyncMetadata _metadata;

    public InMemorySyncMetadataStore(string deviceId)
    {
        ArgumentException.ThrowIfNullOrEmpty(deviceId);
        _metadata = SyncMetadata.Empty(deviceId);
    }

    public Task<SyncMetadata> GetAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_lock) return Task.FromResult(_metadata);
    }

    public Task SaveAsync(SyncMetadata metadata, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ct.ThrowIfCancellationRequested();
        lock (_lock) _metadata = metadata;
        return Task.CompletedTask;
    }
}
