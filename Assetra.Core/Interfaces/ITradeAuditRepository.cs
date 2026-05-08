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
}
