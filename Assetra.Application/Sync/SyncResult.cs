namespace Assetra.Application.Sync;

/// <summary>
/// <see cref="SyncOrchestrator.SyncAsync"/> 的回傳摘要。給 UI 顯示「上次同步：拉 X 推 Y、Z 件衝突待解決」。
/// </summary>
/// <param name="PulledCount">本次從遠端拉回並寫入本地的 envelope 數（含 tombstone）。</param>
/// <param name="PushedCount">本次成功送上遠端的 envelope 數。</param>
/// <param name="AutoResolvedConflicts">resolver 自動裁決的衝突數（KeepLocal / KeepRemote）。</param>
/// <param name="ManualConflicts">resolver 回 Manual、需 UI 介入的衝突數。</param>
/// <param name="CompletedAt">本次同步完成的 UTC 時間（即新的 <c>SyncMetadata.LastSyncAt</c>）。</param>
public sealed record SyncResult(
    int PulledCount,
    int PushedCount,
    int AutoResolvedConflicts,
    int ManualConflicts,
    DateTimeOffset CompletedAt);
