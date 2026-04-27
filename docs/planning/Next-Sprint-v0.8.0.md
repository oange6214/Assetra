# v0.8.0 Sprint Plan — Import Governance Phase 2

> 範圍：2–3 週。在 v0.7.0 CSV/Excel 匯入 MVP 之上，補齊「自動分類規則」與「匯入歷史 + rollback」兩條主線。
> 對帳（reconciliation）獨立成 v0.9 sprint，不在本期範圍。

## 一、版本目標

| # | 項目 | Bounded Context | 預估 |
|---|------|---|---|
| F1 | `ImportRule` 自動分類規則（counterparty / memo 關鍵字 → ExpenseCategory + Note 樣板） | Importing / Budgeting | M |
| F2 | 匯入歷史保存（`ImportBatchHistory`，最近 N 筆 batch + 套用結果） | Importing | M |
| F3 | 匯入 rollback（依 batch 整批撤銷已套用的 trades） | Importing / Portfolio | M |
| D1 | 動工前技術債（見 §三） | 全層 | S |

## 二、缺口全景（v0.8.0 之後路線）

### P0（本 sprint 範圍）
- F1 `ImportRule`：`Apply` 階段在 `MapToTrade` 之前查 rule，命中則套 `CategoryId` + Note 樣板
- F2 匯入歷史：`ImportBatch` 套用後寫入 `ImportBatchHistory` 表，含 trade id 對應
- F3 Rollback：依 historyId 反向刪除/還原（Overwrite 列需保留被覆蓋前的 trade snapshot）

### P1（v0.9 之後）
- 對帳（Reconciliation）：本期匯入 vs 既有交易差額報告 — L
- 投資績效（XIRR / TWR / MWR）— M
- 報表 PDF / CSV export — M
- 風險分析（波動度、最大回撤、Sharpe、集中度）— M

### P2（範圍邊緣）
- PDF 對帳單匯入（deterministic parser）— L
- 外幣 / 美股 pipeline — L
- 雲端同步 — XL

## 三、動工前要先處理的技術債

### D1-1 補 `ImportApplyServiceTests` 對 Overwrite 路徑的覆蓋
F3 rollback 需要還原被 Overwrite 刪掉的 trade。先補 Overwrite 行為的測試覆蓋，避免 rollback 拿錯 snapshot。

### D1-2 抽 `ImportApplyService.MapToTrade` 為獨立 mapper
F1 要在 mapping 之前注入 rule 結果；現在 mapping 跟 apply 綁在一起，先抽 `IImportRowMapper` 或 static helper，commit 不變行為。

## 四、F1 / F2 / F3 設計要點

### F1 ImportRule

#### Core 模型
```
ImportRule(
    Id: Guid,
    Name: string,
    Priority: int,                  // 數字小者先套
    MatchField: ImportRuleField,    // Counterparty | Memo | CounterpartyOrMemo
    Pattern: string,                // 包含字串；後續可擴 regex
    MatchKind: ImportRuleMatchKind, // Contains | StartsWith | Equals | Regex
    CategoryId: Guid?,              // 套用後寫到 trade.CategoryId
    NoteTemplate: string?,          // 例 "{counterparty} - 訂閱"
    IsEnabled: bool,
    CreatedAt: DateTimeOffset)
```

#### 套用點
- `ImportApplyService.MapToTrade` 之前先呼叫 `IImportRuleEngine.Match(row)` 取最高優先 rule
- 命中 → 用 rule 的 Category 與 NoteTemplate 覆寫預設行為
- 未命中 → 走原 v0.7 邏輯

#### UI（最小可用）
- `Features/Import/Rules/ImportRulesView`：list + 新增 / 編輯 / 啟停
- 不在 v0.8 做的：rule 預覽試算、批次匯入測試、自動學習

### F2 ImportBatchHistory

#### Core 模型
```
ImportBatchHistory(
    Id: Guid,
    BatchId: Guid,             // 同 ImportBatch.Id
    FileName: string,
    Format: ImportFormat,
    AppliedAt: DateTimeOffset,
    RowsApplied: int,
    RowsSkipped: int,
    RowsOverwritten: int,
    Entries: IReadOnlyList<ImportBatchEntry>)

ImportBatchEntry(
    RowIndex: int,
    Action: ImportBatchAction,        // Added | Overwritten | Skipped
    NewTradeId: Guid?,
    OverwrittenTradeSnapshot: TradeSnapshot?)  // F3 rollback 需要
```

#### 持久化
- 新表 `ImportBatchHistory` + `ImportBatchEntry`（SQLite WAL）
- 預設保留最近 50 筆 batch；超過自動 GC（保留條數可從設定調）

#### 套用點
- `ImportApplyService.ApplyAsync` 末段寫一筆 history（取代原本只回 `ImportApplyResult`）

### F3 Rollback

#### Service
```
IImportRollbackService.RollbackAsync(Guid historyId, CancellationToken)
  → ImportRollbackResult(Reverted, Restored, Failed)
```

#### 行為
- `Added` 列：刪除 `NewTradeId`
- `Overwritten` 列：刪除 `NewTradeId` + 用 `OverwrittenTradeSnapshot` 還原舊 trade
- `Skipped` 列：no-op
- 全段在 transaction 內；任一筆失敗 → 整批 abort + 回 Failed

#### UI
- `ImportView` footer 加「最近匯入歷史」抽屜（drawer / flyout）
- 每筆 history 列出檔名、時間、套用筆數，提供 `Rollback` button + 二次確認

## 五、明確排除（依 CLAUDE.md）

下列不在 Assetra 路線圖：
- AI 自動分類（LLM 推測 ExpenseCategory）
- LLM-based OCR / PDF 解析
- 跨用戶 rule 共享 / market place

deterministic regex / keyword rule、本地 history / rollback 屬財務範疇，**保留**。

## 六、建議實作順序

1. **D1-1**（Overwrite 測試）— 為 F3 鋪路
2. **D1-2**（抽 mapper）— 為 F1 鋪路
3. **F2**（history 持久化）— F3 的前置條件，先做
4. **F3**（rollback service + UI）— 有 history 後就能做
5. **F1**（ImportRule 引擎 + UI）— 獨立子題，最後做避免阻塞 F2/F3

## 七、Definition of Done（v0.8.0）

- [ ] CI 綠（`dotnet build` 0 errors / 0 warnings、`dotnet test` 全綠）
- [ ] `Assetra.Tests` 覆蓋 F1/F2/F3 服務層 happy path + Rollback 含 Overwrite 還原
- [ ] CHANGELOG 補上 v0.8.0 條目
- [ ] `docs/architecture/Bounded-Contexts.md` Importing context 標記更新（`ImportRule` ✅、`ImportBatchHistory` ✅、Reconciliation 仍 ⏳ v0.9+）
- [ ] `docs/planning/Implementation-Roadmap.md` 與 `Assetra-Feature-Blueprint-and-Roadmap.md` §K 同步狀態
- [ ] zh-TW + en-US 語言檔補齊新 UI 字串
