namespace Assetra.Core.Models.Sync;

/// <summary>
/// 本裝置的同步狀態。每台裝置一份；存於 AppSettings 或獨立 keystore。
/// </summary>
/// <param name="DeviceId">裝置唯一識別（GUID 字串）；首次啟動 cloud sync 時產生並持久化。</param>
/// <param name="LastSyncAt">最後一次成功同步完成的 UTC 時間；null = 從未同步。</param>
/// <param name="Cursor">
/// Provider-specific 增量游標（例如 ETag、page token、change feed sequence）。
/// 由 <see cref="Assetra.Core.Interfaces.Sync.ICloudSyncProvider"/> 解讀；上層只負責保存。
/// </param>
public sealed record SyncMetadata(
    string DeviceId,
    DateTimeOffset? LastSyncAt = null,
    string? Cursor = null)
{
    public static SyncMetadata Empty(string deviceId) => new(deviceId);
}
