using Assetra.Core.Models;

namespace Assetra.Core.Interfaces;

/// <summary>
/// Append-only repository for <see cref="TradeAuditEntry"/>. Implementations
/// must guarantee never to overwrite or delete an existing row — the table is
/// the user's only paper trail when they regret an edit.
///
/// Optional dependency: <see cref="TradeDeletionWorkflowService"/> writes here
/// when the implementation is supplied (DI-registered) and silently no-ops
/// otherwise. This keeps the audit feature opt-in and avoids forcing every
/// test fixture / null-service stub to provide an implementation.
/// </summary>
public interface ITradeAuditRepository
{
    /// <summary>Insert a new audit row. Throws on duplicate <c>Id</c>.</summary>
    Task AppendAsync(TradeAuditEntry entry, CancellationToken ct = default);

    /// <summary>
    /// Read the most recent <paramref name="limit"/> audit entries, descending
    /// by <see cref="TradeAuditEntry.RecordedAt"/>. Powers the audit-log viewer.
    /// Default fallback returns empty (audit repo is optional).
    /// </summary>
    Task<IReadOnlyList<TradeAuditEntry>> GetRecentAsync(int limit = 100, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<TradeAuditEntry>>(Array.Empty<TradeAuditEntry>());
}
