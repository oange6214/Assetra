using Assetra.Core.Models.Sync;

namespace Assetra.Core.Interfaces.Sync;

/// <summary>
/// FinancialGoal ↔ sync layer adapter. See <see cref="ICategorySyncStore"/>
/// for the contract description — this interface is the same shape for
/// the Goal domain so the CompositeLocalChangeQueue can route uniformly.
/// </summary>
public interface IFinancialGoalSyncStore
{
    Task<IReadOnlyList<SyncEnvelope>> GetPendingPushAsync(CancellationToken ct = default);
    Task MarkPushedAsync(IReadOnlyList<Guid> ids, CancellationToken ct = default);
    Task ApplyRemoteAsync(IReadOnlyList<SyncEnvelope> envelopes, CancellationToken ct = default);
}
