namespace Assetra.Core.Models;

/// <summary>
/// Append-only audit row capturing the BEFORE state of a Trade right before
/// it gets deleted (or replaced via the edit-recreate flow).
///
/// Stored in the <c>trade_audit</c> table; never updated, never deleted.
/// Provides a recovery path when a user notices "I edited this and lost the
/// original" hours later — they can re-create the trade from the captured JSON.
///
/// MVP scope: schema + write path only. A read/UI viewer is not built; users
/// who need to inspect history can SQL-query the table directly.
/// </summary>
/// <param name="Id">Surrogate audit-row id (different from <see cref="TradeId"/>).</param>
/// <param name="TradeId">The trade that was deleted/replaced.</param>
/// <param name="Action">
/// <c>"delete"</c> for explicit user-initiated deletion;
/// <c>"edit-replace"</c> when the deletion is part of the edit-recreate flow.
/// </param>
/// <param name="TradeJson">Pre-deletion JSON snapshot of the <see cref="Trade"/> record.</param>
/// <param name="RecordedAt">UTC timestamp of when the audit row was written.</param>
/// <param name="Note">Optional free-form context (e.g. "Buy edit replaced trade").</param>
public sealed record TradeAuditEntry(
    Guid Id,
    Guid TradeId,
    string Action,
    string TradeJson,
    DateTime RecordedAt,
    string? Note);
