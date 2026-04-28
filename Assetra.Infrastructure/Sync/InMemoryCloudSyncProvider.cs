using System.Globalization;
using Assetra.Core.Interfaces.Sync;
using Assetra.Core.Models.Sync;

namespace Assetra.Infrastructure.Sync;

/// <summary>
/// 純記憶體 <see cref="ICloudSyncProvider"/>：把所有 envelope 存在 process-local 字典裡，
/// 用單調遞增 sequence 當 cursor。給單元測試 / 整合測試 / 開發期 dogfood 使用，
/// 不做任何 I/O、不加密、不持久化。
/// <para>
/// Push 期間以 <see cref="EntityVersion.Version"/> 比較：incoming.Version &gt; stored.Version 才接受；
/// 否則回傳 conflict 讓 caller 走 <see cref="IConflictResolver"/>。
/// </para>
/// </summary>
public sealed class InMemoryCloudSyncProvider : ICloudSyncProvider
{
    private readonly object _lock = new();
    private readonly Dictionary<Guid, StoredEnvelope> _store = new();
    private long _sequence;

    private readonly record struct StoredEnvelope(SyncEnvelope Envelope, long Sequence);

    public Task<SyncPullResult> PullAsync(SyncMetadata metadata, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ct.ThrowIfCancellationRequested();

        lock (_lock)
        {
            var sinceSeq = ParseCursor(metadata.Cursor);
            var changes = _store.Values
                .Where(e => e.Sequence > sinceSeq)
                .OrderBy(e => e.Sequence)
                .Select(e => e.Envelope)
                .ToList();

            var nextCursor = changes.Count == 0
                ? metadata.Cursor
                : FormatCursor(_sequence);

            return Task.FromResult(new SyncPullResult(changes, nextCursor));
        }
    }

    public Task<SyncPushResult> PushAsync(
        SyncMetadata metadata,
        IReadOnlyList<SyncEnvelope> envelopes,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(envelopes);
        ct.ThrowIfCancellationRequested();

        var accepted = new List<Guid>(envelopes.Count);
        var conflicts = new List<SyncConflict>();

        lock (_lock)
        {
            foreach (var incoming in envelopes)
            {
                if (_store.TryGetValue(incoming.EntityId, out var existing))
                {
                    if (incoming.Version.Version <= existing.Envelope.Version.Version)
                    {
                        conflicts.Add(new SyncConflict(Local: incoming, Remote: existing.Envelope));
                        continue;
                    }
                }

                _sequence++;
                _store[incoming.EntityId] = new StoredEnvelope(incoming, _sequence);
                accepted.Add(incoming.EntityId);
            }

            return Task.FromResult(new SyncPushResult(accepted, conflicts, FormatCursor(_sequence)));
        }
    }

    private static long ParseCursor(string? cursor) =>
        long.TryParse(cursor, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seq) ? seq : 0;

    private static string FormatCursor(long seq) => seq.ToString(CultureInfo.InvariantCulture);
}
