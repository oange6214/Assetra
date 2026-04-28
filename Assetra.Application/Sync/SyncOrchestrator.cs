using Assetra.Core.Interfaces.Sync;
using Assetra.Core.Models.Sync;

namespace Assetra.Application.Sync;

/// <summary>
/// 把 <see cref="ICloudSyncProvider"/>、<see cref="ILocalChangeQueue"/>、<see cref="ISyncMetadataStore"/>、
/// <see cref="IConflictResolver"/> 串成完整 pull → resolve → push → save 流程。
/// <para>
/// 演算法：
/// <list type="number">
///   <item>讀取本裝置 <see cref="SyncMetadata"/>。</item>
///   <item>Pull：拿遠端 cursor 之後的 envelopes → <see cref="ILocalChangeQueue.ApplyRemoteAsync"/>。</item>
///   <item>讀取本端待 push 的 envelopes → <see cref="ICloudSyncProvider.PushAsync"/>。</item>
///   <item>對 push 回來的 conflicts 套 <see cref="IConflictResolver"/>：
///     <list type="bullet">
///       <item><c>KeepLocal</c>：以 remote.Version+1 重新 push（一次重試）。</item>
///       <item><c>KeepRemote</c>：把 remote 寫進本地、不再 push。</item>
///       <item><c>Manual</c>：丟入 <see cref="ILocalChangeQueue.RecordManualConflictAsync"/> 給 UI。</item>
///     </list>
///   </item>
///   <item>更新並儲存 cursor / LastSyncAt。</item>
/// </list>
/// </para>
/// <para>
/// 失敗策略：任何一步 throw 都讓例外往上冒（caller 決定 retry / 顯示 toast）；不在這裡吞例外或寫部分狀態
/// — local change queue 的 mutation（ApplyRemoteAsync、MarkPushedAsync）必須以 idempotent 設計，重跑無副作用。
/// </para>
/// </summary>
public sealed class SyncOrchestrator
{
    private readonly ICloudSyncProvider _provider;
    private readonly ILocalChangeQueue _queue;
    private readonly ISyncMetadataStore _metadataStore;
    private readonly IConflictResolver _resolver;
    private readonly TimeProvider _time;

    public SyncOrchestrator(
        ICloudSyncProvider provider,
        ILocalChangeQueue queue,
        ISyncMetadataStore metadataStore,
        IConflictResolver resolver,
        TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(metadataStore);
        ArgumentNullException.ThrowIfNull(resolver);

        _provider = provider;
        _queue = queue;
        _metadataStore = metadataStore;
        _resolver = resolver;
        _time = time ?? TimeProvider.System;
    }

    public async Task<SyncResult> SyncAsync(CancellationToken ct = default)
    {
        var metadata = await _metadataStore.GetAsync(ct).ConfigureAwait(false);

        var pull = await _provider.PullAsync(metadata, ct).ConfigureAwait(false);
        if (pull.Envelopes.Count > 0)
            await _queue.ApplyRemoteAsync(pull.Envelopes, ct).ConfigureAwait(false);

        var pending = await _queue.GetPendingAsync(ct).ConfigureAwait(false);
        var push = pending.Count == 0
            ? new SyncPushResult(Array.Empty<Guid>(), Array.Empty<SyncConflict>(), pull.NextCursor)
            : await _provider.PushAsync(metadata, pending, ct).ConfigureAwait(false);

        if (push.Accepted.Count > 0)
            await _queue.MarkPushedAsync(push.Accepted, ct).ConfigureAwait(false);

        var (autoResolved, manualConflicts) = await ResolveConflictsAsync(metadata, push.Conflicts, ct)
            .ConfigureAwait(false);

        var now = _time.GetUtcNow();
        var nextCursor = push.NextCursor ?? pull.NextCursor ?? metadata.Cursor;
        await _metadataStore
            .SaveAsync(metadata with { LastSyncAt = now, Cursor = nextCursor }, ct)
            .ConfigureAwait(false);

        return new SyncResult(
            PulledCount: pull.Envelopes.Count,
            PushedCount: push.Accepted.Count,
            AutoResolvedConflicts: autoResolved,
            ManualConflicts: manualConflicts.Count,
            CompletedAt: now);
    }

    private async Task<(int AutoResolved, IReadOnlyList<SyncConflict> Manual)> ResolveConflictsAsync(
        SyncMetadata metadata,
        IReadOnlyList<SyncConflict> conflicts,
        CancellationToken ct)
    {
        if (conflicts.Count == 0)
            return (0, Array.Empty<SyncConflict>());

        var keepLocalRetries = new List<SyncEnvelope>();
        var keepRemoteAdopts = new List<SyncEnvelope>();
        var manual = new List<SyncConflict>();
        var autoResolved = 0;

        foreach (var conflict in conflicts)
        {
            switch (_resolver.Resolve(conflict))
            {
                case SyncResolution.KeepLocal:
                    keepLocalRetries.Add(conflict.Local with
                    {
                        Version = conflict.Local.Version with
                        {
                            Version = conflict.Remote.Version.Version + 1,
                            LastModifiedAt = _time.GetUtcNow(),
                            LastModifiedByDevice = metadata.DeviceId,
                        },
                    });
                    autoResolved++;
                    break;
                case SyncResolution.KeepRemote:
                    keepRemoteAdopts.Add(conflict.Remote);
                    autoResolved++;
                    break;
                default:
                    manual.Add(conflict);
                    break;
            }
        }

        if (keepRemoteAdopts.Count > 0)
            await _queue.ApplyRemoteAsync(keepRemoteAdopts, ct).ConfigureAwait(false);

        if (keepLocalRetries.Count > 0)
        {
            var retryResult = await _provider.PushAsync(metadata, keepLocalRetries, ct).ConfigureAwait(false);
            if (retryResult.Accepted.Count > 0)
                await _queue.MarkPushedAsync(retryResult.Accepted, ct).ConfigureAwait(false);
            // 二次 conflict 不再自動處理 — 進 manual 待 UI 介入，避免無限 retry
            foreach (var c in retryResult.Conflicts)
                manual.Add(c);
        }

        if (manual.Count > 0)
            await _queue.RecordManualConflictAsync(manual, ct).ConfigureAwait(false);

        return (autoResolved, manual);
    }
}
