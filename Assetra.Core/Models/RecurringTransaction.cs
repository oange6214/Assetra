namespace Assetra.Core.Models;

public enum RecurrenceFrequency
{
    Daily,
    Weekly,
    BiWeekly,
    Monthly,
    Quarterly,
    Yearly,
}

public enum AutoGenerationMode
{
    /// <summary>到期自動產生並寫入 Trade。</summary>
    AutoApply,
    /// <summary>到期僅建立 PendingRecurringEntry，待使用者確認。</summary>
    PendingConfirm,
}

/// <summary>
/// 訂閱／週期性交易設定（房租、Netflix、薪資等）。
/// </summary>
public sealed record RecurringTransaction(
    Guid Id,
    string Name,
    TradeType TradeType,
    decimal Amount,
    Guid? CashAccountId,
    Guid? CategoryId,
    RecurrenceFrequency Frequency,
    int Interval,
    DateTime StartDate,
    DateTime? EndDate,
    AutoGenerationMode GenerationMode,
    DateTime? LastGeneratedAt = null,
    DateTime? NextDueAt = null,
    string? Note = null,
    bool IsEnabled = true);
