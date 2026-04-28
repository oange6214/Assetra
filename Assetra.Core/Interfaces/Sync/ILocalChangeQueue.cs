using Assetra.Core.Models.Sync;

namespace Assetra.Core.Interfaces.Sync;

/// <summary>
/// 本端 entity ↔ <see cref="SyncEnvelope"/> 的橋樑：
/// <list type="bullet">
///   <item><see cref="GetPendingAsync"/>：列出待 push 的本端變更（version 比上次同步新的 entity）。</item>
///   <item><see cref="MarkPushedAsync"/>：標記某批 entity 已成功 push、不需再送。</item>
///   <item><see cref="ApplyRemoteAsync"/>：把遠端拉到的變更寫進本端（含 deleted tombstone）。</item>
///   <item><see cref="RecordManualConflictAsync"/>：resolver 判 Manual 時、把衝突丟進 UI 待解決佇列。</item>
/// </list>
/// 本介面是 <see cref="Assetra.Application.Sync.SyncOrchestrator"/> 與「實際 entity 倉庫 + 變更 outbox」之間的抽象，
/// v0.20.3 提供 in-memory 實作給測試 / orchestrator 開發；v0.20.4+ 才會做 SQLite 版本對接真 entity。
/// </summary>
public interface ILocalChangeQueue
{
    Task<IReadOnlyList<SyncEnvelope>> GetPendingAsync(CancellationToken ct = default);

    Task MarkPushedAsync(IReadOnlyList<Guid> entityIds, CancellationToken ct = default);

    Task ApplyRemoteAsync(IReadOnlyList<SyncEnvelope> envelopes, CancellationToken ct = default);

    Task RecordManualConflictAsync(IReadOnlyList<SyncConflict> conflicts, CancellationToken ct = default);
}
