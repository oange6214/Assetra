using System.Text.Json;
using Assetra.Core.Interfaces.Sync;
using Assetra.Core.Models.Sync;

namespace Assetra.Infrastructure.Sync;

/// <summary>
/// 把 <see cref="SyncMetadata"/> 持久化到單一 JSON 檔（例：<c>%APPDATA%\Assetra\sync-meta.json</c>）。
/// <para>
/// 寫入採 atomic-rename：先寫 .tmp、再 <see cref="File.Move(string, string, bool)"/> 覆寫，避免 process crash 留下截斷檔。
/// 並發以 <c>SemaphoreSlim</c> 序列化（單行程內），跨行程不保護——cloud sync 由 <see cref="SyncOrchestrator"/>
/// 串列呼叫，不會跨行程競爭。
/// </para>
/// </summary>
public sealed class JsonSyncMetadataStore : ISyncMetadataStore, IDisposable
{
    private readonly string _path;
    private readonly string _defaultDeviceId;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public JsonSyncMetadataStore(string path, string defaultDeviceId)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentException.ThrowIfNullOrEmpty(defaultDeviceId);
        _path = path;
        _defaultDeviceId = defaultDeviceId;
    }

    public async Task<SyncMetadata> GetAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_path)) return SyncMetadata.Empty(_defaultDeviceId);
            await using var fs = File.OpenRead(_path);
            var dto = await JsonSerializer.DeserializeAsync<MetadataDto>(fs, JsonOptions, ct)
                .ConfigureAwait(false);
            if (dto is null) return SyncMetadata.Empty(_defaultDeviceId);
            return new SyncMetadata(
                DeviceId: string.IsNullOrEmpty(dto.DeviceId) ? _defaultDeviceId : dto.DeviceId,
                LastSyncAt: dto.LastSyncAt,
                Cursor: dto.Cursor);
        }
        finally { _gate.Release(); }
    }

    public async Task SaveAsync(SyncMetadata metadata, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path) ?? ".");
            var tmp = _path + ".tmp";
            await using (var fs = File.Create(tmp))
            {
                await JsonSerializer
                    .SerializeAsync(fs, new MetadataDto(metadata.DeviceId, metadata.LastSyncAt, metadata.Cursor), JsonOptions, ct)
                    .ConfigureAwait(false);
            }
            File.Move(tmp, _path, overwrite: true);
        }
        finally { _gate.Release(); }
    }

    public void Dispose() => _gate.Dispose();

    private sealed record MetadataDto(string DeviceId, DateTimeOffset? LastSyncAt, string? Cursor);
}
