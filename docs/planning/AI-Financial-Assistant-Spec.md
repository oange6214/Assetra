# AI 財務助手 — 設計與實作 Spec (v0.27+)

**Status:** Phase 1 shipped (rule-based, no LLM).
**Last updated:** 2026-05-09

---

## 目標

讓使用者用自然語言查詢個人財務狀態 — "我目前淨資產多少？"、"上個月還剩多少預算？" — 而不必導航到對應頁面手動找。

## 三階段路線

### Phase 1 — 規則式分發（已實作）

- 介面：`Assetra.Core.Interfaces.IFinancialAssistant`
- 實作：`Assetra.Application.Assistant.RuleBasedFinancialAssistant`
- ViewModel：`Assetra.WPF.Features.Assistant.AssistantViewModel`
- DI：`AddAssistantContext()` in `AppBootstrapper.cs`

**支援查詢**（regex match → 直接呼叫 query service）：
- 「淨資產」/ `net worth` → `IBalanceQueryService.GetAllCashBalancesAsync` + `GetAllLiabilitySnapshotsAsync` 加總
- 「現金」/ `cash balance` → 列出所有 cash account 餘額（top 8）
- 「負債」/ `liability` → 加總 + 償還百分比
- 「AMT」/「海外所得」 → 提示申報門檻 + Settings.Amt 參數

**特性：**
- 完全 deterministic — 無 LLM 依賴、無 API key 需求
- 直接呼叫現有 query service，不重複實作邏輯
- 失敗時回 `FinancialAssistantResponse.Unhandled` + 建議查詢清單

### Phase 2 — 摘要與提醒（未實作）

- 月底自動摘要：`MonthEndSummaryService` 每月 1 號 schedule trigger
- 預算超支預警：query `IBudgetRepository` + `IBalanceQueryService` 比對
- 訂閱續扣提醒：query `IRecurringTransactionRepository.NextDue`

合適時機觸發（背景 hosted service），結果丟到 `INotificationService` 或 `ISnackbarService`。

### Phase 3 — LLM 規劃建議（未實作）

- 抽 `ILlmProvider` 介面（`AskAsync(prompt, ct)` → string）
- 實作：`OpenAiLlmProvider`（HttpClient + API key from settings）
  / `LocalOllamaProvider`（http://localhost:11434）
- `LlmFinancialAssistant` 把 user query + 結構化 context（最近 30 天 trade summary、
  current portfolio snapshot）丟給 LLM，讓它產生 actionable plan。
- Tool calls：LLM 可呼叫具名 query function（`GetCashBalance`、`GetTaxSummary`），
  避免 hallucinate 數字。

**安全考量：**
- API key 存於 AppSettings，UI 加 redact display
- prompt 不包含個人 PII（帳戶名、金額正規化為 anonymous tokens）
- response cache：相同 query 5 分鐘內回 cache，省 token

---

## 介面契約

```csharp
public interface IFinancialAssistant
{
    Task<FinancialAssistantResponse> AnswerAsync(FinancialAssistantQuery query, CancellationToken ct = default);
    IReadOnlyList<string> SuggestedQueries { get; }
}

public sealed record FinancialAssistantQuery(string Text, string Locale = "zh-TW");
public sealed record FinancialAssistantResponse(string Answer, string Source = "", bool IsHandled = true);
```

Phase 2/3 維持同一介面 — 替換實作即可，UI 不變。

---

## UI 規劃

- **NavSection.Assistant**（新；待加入）— 主側欄第二排（Reports 之後）
- **AssistantView.xaml** — 對話列表 + 輸入框 + 建議查詢按鈕
- 對話訊息分使用者 / 助手兩種樣式；助手訊息底部顯示 `Source` badge
- 空白狀態：顯示 `SuggestedQueries` 為可點擊 chip

---

## 測試策略

- `RuleBasedFinancialAssistantTests`：每條規則一個 unit test
  + edge case（無交易資料、空輸入、未知模式）
- `AssistantViewModelTests`：Send command flow（input → message added →
  IsAnswering toggle → response message added）
- 之後 Phase 3 LLM provider 用 mock HttpClient 測 prompt 組裝 + response parsing

---

## Roadmap 對位

| Roadmap milestone | Phase | 預估 |
|---|---|---|
| v0.27 (current) | Phase 1 ✅ | 完成 |
| v0.28 | Phase 2 (摘要/提醒) | 8–12h |
| v0.29 | Phase 3 (LLM) | 16–24h（含 provider 抽象 + 測試） |
