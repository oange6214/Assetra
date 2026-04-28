namespace Assetra.Core.Models.Sync;

/// <summary>
/// 同步 metadata：每個受控於雲端同步的 entity 都帶這個欄位三人組。
/// <para>
/// • <see cref="Version"/> — 單調遞增的整數，每次 mutation +1，用於偵測平行修改。
/// • <see cref="LastModifiedAt"/> — UTC 時間戳，做 last-write-wins 判斷的主鍵。
/// • <see cref="LastModifiedByDevice"/> — 修改源裝置 id，純供 audit / 衝突 UI 顯示，不參與比較。
/// </para>
/// 預設值（Version=0、UnixEpoch、空字串）代表「從未同步過」。
/// </summary>
public sealed record EntityVersion(
    long Version = 0,
    DateTimeOffset LastModifiedAt = default,
    string LastModifiedByDevice = "")
{
    public static EntityVersion Initial(string deviceId, DateTimeOffset now) =>
        new(Version: 1, LastModifiedAt: now, LastModifiedByDevice: deviceId ?? string.Empty);

    public EntityVersion Bump(string deviceId, DateTimeOffset now) =>
        this with
        {
            Version = Version + 1,
            LastModifiedAt = now,
            LastModifiedByDevice = deviceId ?? string.Empty,
        };
}
