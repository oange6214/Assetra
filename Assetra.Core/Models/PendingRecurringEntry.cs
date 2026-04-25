namespace Assetra.Core.Models;

public enum PendingStatus
{
    Pending,
    Confirmed,
    Skipped,
}

/// <summary>
/// 待確認的週期性交易：由 RecurringTransaction (PendingConfirm 模式) 排程器產生。
/// 確認後寫入 Trade 並標記 Confirmed。
/// </summary>
public sealed record PendingRecurringEntry(
    Guid Id,
    Guid RecurringSourceId,
    DateTime DueDate,
    decimal Amount,
    TradeType TradeType,
    Guid? CashAccountId,
    Guid? CategoryId,
    string? Note,
    PendingStatus Status = PendingStatus.Pending,
    Guid? GeneratedTradeId = null,
    DateTime? ResolvedAt = null);
