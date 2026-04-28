namespace Assetra.Core.Models;

/// <summary>
/// 目標撥款規則：定期從來源帳戶撥款到目標。對應到 <see cref="RecurringTransaction"/>，但保持為獨立
/// 領域物件以便 Goals 子系統獨立演進；可在後續 sprint 由 scheduler 將其物化為 RecurringTransaction。
/// </summary>
/// <param name="Id">主鍵。</param>
/// <param name="GoalId">關聯 <see cref="FinancialGoal"/>。</param>
/// <param name="Amount">每次撥款金額（base currency）。必須 &gt; 0。</param>
/// <param name="Frequency">撥款頻率，沿用 <see cref="RecurrenceFrequency"/>。</param>
/// <param name="SourceCashAccountId">來源現金帳戶 — null 表示尚未指定（UI / scheduler 應跳過此規則）。</param>
/// <param name="StartDate">第一次撥款日期。</param>
/// <param name="EndDate">最後一次撥款日期，null 表示無限期（直到目標達成）。</param>
/// <param name="IsEnabled">停用後 scheduler 不再產生對應的 RecurringTransaction。</param>
public sealed record GoalFundingRule(
    Guid Id,
    Guid GoalId,
    decimal Amount,
    RecurrenceFrequency Frequency,
    Guid? SourceCashAccountId,
    DateOnly StartDate,
    DateOnly? EndDate,
    bool IsEnabled = true);
