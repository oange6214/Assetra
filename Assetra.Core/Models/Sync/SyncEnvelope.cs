namespace Assetra.Core.Models.Sync;

/// <summary>
/// Provider-agnostic 同步封包：帶版本資訊的 entity payload。
/// <para>
/// Payload 統一以 JSON 字串攜帶（讓 <see cref="Assetra.Core.Interfaces.Sync.ICloudSyncProvider"/>
/// 不需 generic over T，能處理多 entity type 的混合批次）。實際 (de)serialization 由 caller / mapper 負責，
/// 同步層僅做 envelope 傳遞與衝突偵測。
/// </para>
/// </summary>
/// <param name="EntityId">Entity 主鍵（不論型別一律以 GUID 編碼，未來新 entity 都應改用 Guid 主鍵）。</param>
/// <param name="EntityType">Entity 型別代號（例：<c>"Trade"</c>、<c>"Category"</c>）；caller 用來決定 mapper。</param>
/// <param name="PayloadJson">序列化後的 entity；<see cref="Deleted"/> = true 時可為空字串（tombstone）。</param>
/// <param name="Version">同步 metadata：版本號 / 修改時間 / 來源裝置。</param>
/// <param name="Deleted">墓碑旗標：true 表示遠端 / 本端已刪除此 entity。</param>
public sealed record SyncEnvelope(
    Guid EntityId,
    string EntityType,
    string PayloadJson,
    EntityVersion Version,
    bool Deleted = false);
