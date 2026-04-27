using Assetra.Core.Models.Import;

namespace Assetra.Core.Models.Reconciliation;

/// <summary>
/// 單筆對帳差異。
/// <para>
/// <see cref="StatementRow"/> 與 <see cref="TradeId"/> 至少一者非空：
/// Missing → 僅 StatementRow；Extra → 僅 TradeId；AmountMismatch → 兩者皆非空。
/// </para>
/// </summary>
public sealed record ReconciliationDiff(
    Guid Id,
    Guid SessionId,
    ReconciliationDiffKind Kind,
    ImportPreviewRow? StatementRow,
    Guid? TradeId,
    ReconciliationDiffResolution Resolution,
    DateTimeOffset? ResolvedAt,
    string? Note);
