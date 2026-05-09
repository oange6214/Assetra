# AI 財務助手 — 設計與實作 Spec (v0.27+)

**Status:** Core shipped in v0.27.0 (rule-based + hybrid LLM fallback + insights + history/export). Action automation remains deferred.
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

### Phase 2 — 摘要與提醒（已實作核心）

- 服務：`IAssistantInsightService`
- 實作：`RuleBasedAssistantInsightService`
- Hosted service：`AssistantInsightHostedService`
- UI：`AssistantViewModel.LoadInsightsAsync()` 將 insight cards 顯示於 Assistant 頁面
- 目前涵蓋：預算超支、訂閱/排程到期、月度變動提示

後續可再擴充到 email / push，但不列入 v0.28 release 範圍。

### Phase 3 — LLM fallback / grounded answer（已實作核心）

- 抽 `ILlmProvider` 介面（`AskAsync(prompt, ct)` → string）
- 實作：`OpenAiLlmProvider`（HttpClient + API key from settings）
  / `OllamaLlmProvider`（http://localhost:11434）/ `NullLlmProvider`
- `HybridFinancialAssistant` 先走 deterministic rule-based answer；只有未命中且 provider 已設定時才使用 LLM。
- `GroundedAssistantToolRegistry` 提供只讀 tool context。v0.27 baseline 會先取短快照後拼入 system prompt；不是正式 function-calling 協定。
- 對話歷史可保留於 UI session，並可匯出 Markdown。

Action automation / mutate tools 仍延後至 v1.2，需 confirmation-flow 與安全審查。

**安全考量：**
- API key 目前存於 AppSettings；OS 憑證庫 migration 列在 v1.1 planning。
- Prompt 只能包含短版 grounded snapshot，不可暴露 raw DB row；新增 tool 前需檢查是否包含可識別帳戶資訊。
- 不支援 mutate / write action；所有資料操作都必須停留在 read-only query。
- Response cache 尚未落地；若未來加入，必須依 provider / model / locale / query / context hash 做明確 key。

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

- **NavSection.Assistant** — 主側欄的 AI 財務助理入口
- **AssistantView.xaml** — 對話列表 + 輸入框 + 建議查詢按鈕
- 對話訊息分使用者 / 助手兩種樣式；助手訊息底部顯示 `Source` badge / grounded context
- 空白狀態：顯示 `SuggestedQueries` 為可點擊 chip
- Insight cards：由 `IAssistantInsightService` 回傳，目前為 best-effort，不阻塞聊天 UI

---

## 測試策略

- `RuleBasedFinancialAssistantTests`：每條規則一個 unit test
  + edge case（無交易資料、空輸入、未知模式）
- `AssistantViewModelTests`：Send command flow（input → message added →
  IsAnswering toggle → response message added）
- 之後 Phase 3 LLM provider 用 mock HttpClient 測 prompt 組裝 + response parsing

---

## Roadmap 對位

| Roadmap milestone | Phase | 狀態 |
|---|---|---|
| v0.27 | Phase 1–3 core | ✅ 已完成 |
| v0.28 | UI / DesignSystem alignment | ✅ 已納入 release sweep |
| v1.1 | OS credential store + insight persistence polish | Deferred |
| v1.2 | Action automation / mutate tools | Deferred，需安全審查 |
