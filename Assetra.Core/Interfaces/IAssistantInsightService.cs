namespace Assetra.Core.Interfaces;

/// <summary>
/// AI 助手 Phase 2 — 主動產生「摘要與提醒」洞察（不需要使用者輸入問題）。
///
/// <para>
/// 與 <see cref="IFinancialAssistant"/> 的差異：那是「被動回答」，這是「主動推送」。
/// UI 在 AssistantView 載入時呼叫 <see cref="GetCurrentInsightsAsync"/>，
/// 把回傳的 <see cref="AssistantInsight"/> 列表渲染為 alert chip / 卡片。
/// </para>
///
/// <para>
/// MVP 規則式實作（<c>RuleBasedAssistantInsightService</c>）：
/// <list type="bullet">
///   <item><b>Budget overspending</b> — 比對 IBudgetRepository 與當月實際支出</item>
///   <item><b>Recurring upcoming</b> — 列出 NextDueAt &lt;= today + 7 天的訂閱</item>
///   <item><b>Month delta</b> — 對比上月與本月淨值變化（簡化版，未來 Phase 2.5 擴充）</item>
/// </list>
/// </para>
/// </summary>
public interface IAssistantInsightService
{
    Task<IReadOnlyList<AssistantInsight>> GetCurrentInsightsAsync(CancellationToken ct = default);
}

/// <summary>主動推送的單筆洞察。</summary>
/// <param name="Severity">提醒等級（Info / Warning / Critical）— UI 用來上色。</param>
/// <param name="Title">短標題（最多 30 字）。</param>
/// <param name="Description">詳細說明（可包含金額、日期）。</param>
/// <param name="Source">產生來源標記（"Budget" / "Recurring" / "MonthDelta" 等）。</param>
public sealed record AssistantInsight(
    AssistantInsightSeverity Severity,
    string Title,
    string Description,
    string Source);

public enum AssistantInsightSeverity
{
    Info = 0,
    Warning = 1,
    Critical = 2,
}
