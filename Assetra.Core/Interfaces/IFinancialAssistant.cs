namespace Assetra.Core.Interfaces;

/// <summary>
/// AI 財務助手 — 接收使用者自然語言查詢，回傳結構化答案。
///
/// <para>
/// MVP（v0.27 spec）：
/// <list type="number">
///   <item><b>Phase 1</b> — 規則式分發（<c>RuleBasedFinancialAssistant</c>）：
///         識別固定查詢模板（淨資產、月支出、現金餘額、特定持股市值），
///         直接呼叫對應的 query service，回傳 deterministic 結果。
///         無 LLM 依賴，立即可用。</item>
///   <item><b>Phase 2</b> — 摘要與提醒建議（待 spec）：
///         月底摘要、預算預警、訂閱續扣提醒。</item>
///   <item><b>Phase 3</b> — 規劃建議（待 LLM provider 抽象）：
///         針對使用者問題透過 LLM 產生 action plan。</item>
/// </list>
/// </para>
///
/// <para>
/// 介面以 <see cref="FinancialAssistantQuery"/>/<see cref="FinancialAssistantResponse"/>
/// 為傳輸契約，避免不同實作（rule-based / LLM / hybrid）耦合到 ViewModel。
/// </para>
/// </summary>
public interface IFinancialAssistant
{
    /// <summary>
    /// 處理一條使用者查詢；回傳結構化回答（含答案文字 + 來源 metadata）。
    /// 無法處理時回傳 <see cref="FinancialAssistantResponse.Unhandled"/>。
    /// </summary>
    Task<FinancialAssistantResponse> AnswerAsync(FinancialAssistantQuery query, CancellationToken ct = default);

    /// <summary>
    /// 取得幾個建議查詢範例（給空白狀態 UI 提示用）。
    /// </summary>
    IReadOnlyList<string> SuggestedQueries { get; }
}

/// <summary>使用者查詢輸入。</summary>
/// <param name="Text">原始輸入文字。</param>
/// <param name="Locale">語系（決定 rule 庫與輸出語言；目前只支援 zh-TW / en-US）。</param>
public sealed record FinancialAssistantQuery(string Text, string Locale = "zh-TW");

/// <summary>查詢回答。</summary>
/// <param name="Answer">回答主文（已格式化、可直接顯示）。</param>
/// <param name="Source">回答來源標記（"BalanceQueryService" / "TaxCalculationService" / "LLM" / "" 等），便於 UI 顯示「資料來源」徽章。</param>
/// <param name="IsHandled">true = 有具體回答；false = 未識別查詢類型。</param>
public sealed record FinancialAssistantResponse(string Answer, string Source = "", bool IsHandled = true)
{
    public static FinancialAssistantResponse Unhandled(string fallbackHint) =>
        new(fallbackHint, Source: "", IsHandled: false);
}
