# v0.9.0 Sprint Plan — Reconciliation

> 範圍：2–3 週。在 v0.8.0「匯入治理 Phase 2」之上，補上「對帳」這塊：使用者把對帳單匯入後，要能跟既有資料庫做差異比對並逐項處置。
> 完成後，import 治理三部曲（preview → apply+rollback → reconcile）形成閉環。

## 一、版本目標

| # | 項目 | Bounded Context | 預估 |
|---|------|---|---|
| F1 | `ReconciliationSession` + `ReconciliationDiff` 模型 + repository | Reconciliation（新） | M |
| F2 | `ReconciliationService`：對帳單列 vs 既有 Trade 比對引擎，產 diff 三類（Missing / Extra / AmountMismatch） | Reconciliation / Importing | M |
| F3 | 對帳 UI：Import 頁加「對帳」分頁，列出 diff + 一鍵動作（建立缺漏 / 標記已解 / 刪除多出） | WPF / Reconciliation | M |
| D1 | 動工前技術債（見 §三） | 全層 | S |

## 二、缺口全景

### P0（本 sprint 範圍）
- F1 模型：`ReconciliationSession(Id, AccountId, PeriodStart, PeriodEnd, SourceBatchId?, CreatedAt, Status)` + `ReconciliationDiff(Id, SessionId, Kind, StatementRow?, TradeId?, Resolution, ResolvedAt?)`
- F2 比對：以「日期 ± 1 天 + 金額（abs，正負號分流）」為主鍵；對帳單有 trade 沒有 → Missing；trade 有對帳單沒有 → Extra；金額差異 → AmountMismatch
- F3 對帳工作台：Import 頁 tab「對帳」新增「開新對帳作業」按鈕（選帳戶 + 匯入歷史 batch 或上傳新檔），下方 DataGrid 三 group（Missing / Extra / AmountMismatch）+ 每列 action

### P1（下一輪）
- 對帳簽收（鎖定 session，進入唯讀狀態，產生對帳報告）
- 多帳戶合併對帳（同期間銀行 + 券商）
- CSV / PDF 對帳報告匯出（屬 Phase 2 報表系統）

### P2（範圍邊緣）
- 自動排程對帳提醒（屬 Recurring 子系統擴充）
- 與 Goals / Budget 對帳異動聯動

## 三、動工前要先處理的技術債

### D1-1 抽 `ImportPreviewRow` 為跨 sprint 共用結構
v0.7 / v0.8 把 `ImportPreviewRow` 視為 import 內部型別。F2 對帳引擎需要直接吃「對帳單原始列」做比對，沿用 `ImportPreviewRow` 即可，不必新增 `StatementRow`，但要把它由 `Models.Import` 暴露在更通用的命名空間（暫時保留現址，於 doc 標明跨 context 共用）。

### D1-2 補 `ImportBatchHistoryRepository.GetEntriesAsync` 查詢 API
F2 需要從 historyId 反查 batch 的所有 ImportBatchEntry 與對應原始 row。目前 SaveAsync 寫入完整 entries，但讀取 API 只回 history metadata。要新增「依 batchId 取出原始 ImportPreviewRow」。

### D1-3 `Trade` 比對 key 抽函式
F2 用「日期 ± 1 天 + 金額」做 fuzzy match。`ImportConflictDetector` 已用 `date | abs(amount) | symbol` 做嚴格 dedupe，但對帳允許日期相鄰（銀行入帳延遲）。先抽 `IReconciliationMatcher` 介面，預設實作 `DefaultReconciliationMatcher`，避免後續多帳戶情境分散邏輯。

## 四、F1 / F2 / F3 設計要點

### F1 ReconciliationSession 模型

```csharp
public enum ReconciliationStatus { Open, Resolved, Archived }

public enum ReconciliationDiffKind { Missing, Extra, AmountMismatch }

public enum ReconciliationDiffResolution { Pending, Created, Deleted, MarkedResolved, Ignored }

public sealed record ReconciliationSession(
    Guid Id,
    Guid AccountId,                        // 對帳基準帳戶（CashAccountId 或 BrokerAccountId）
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    Guid? SourceBatchId,                   // 若由 ImportBatchHistory 衍生
    DateTimeOffset CreatedAt,
    ReconciliationStatus Status,
    string? Note);

public sealed record ReconciliationDiff(
    Guid Id,
    Guid SessionId,
    ReconciliationDiffKind Kind,
    ImportPreviewRow? StatementRow,        // Missing / AmountMismatch 時帶
    Guid? TradeId,                         // Extra / AmountMismatch 時帶
    ReconciliationDiffResolution Resolution,
    DateTimeOffset? ResolvedAt,
    string? Note);
```

#### Repository
- `IReconciliationSessionRepository` — `GetAllAsync`、`GetByIdAsync`、`AddAsync`、`UpdateStatusAsync`、`RemoveAsync`
- `IReconciliationDiffRepository` — 連同 session 一起存（單一 SQLite 表 `reconciliation_diff`，FK → `reconciliation_session.id`），`UpdateResolutionAsync`

#### Schema 遷移
新增兩張表：
```sql
CREATE TABLE reconciliation_session (
    id BLOB PRIMARY KEY,
    account_id BLOB NOT NULL,
    period_start TEXT NOT NULL,
    period_end TEXT NOT NULL,
    source_batch_id BLOB,
    created_at TEXT NOT NULL,
    status INTEGER NOT NULL,
    note TEXT
);
CREATE TABLE reconciliation_diff (
    id BLOB PRIMARY KEY,
    session_id BLOB NOT NULL REFERENCES reconciliation_session(id) ON DELETE CASCADE,
    kind INTEGER NOT NULL,
    statement_row_json TEXT,              -- ImportPreviewRow snapshot
    trade_id BLOB,
    resolution INTEGER NOT NULL,
    resolved_at TEXT,
    note TEXT
);
```

### F2 ReconciliationService

```csharp
public interface IReconciliationService
{
    Task<ReconciliationSession> CreateAsync(
        Guid accountId,
        DateOnly periodStart,
        DateOnly periodEnd,
        IReadOnlyList<ImportPreviewRow> statementRows,
        Guid? sourceBatchId,
        CancellationToken ct = default);

    Task<IReadOnlyList<ReconciliationDiff>> RecomputeAsync(Guid sessionId, CancellationToken ct = default);

    Task ApplyResolutionAsync(Guid diffId, ReconciliationDiffResolution resolution, CancellationToken ct = default);
}
```

#### 比對演算法
1. 載入帳戶 trades（`ITradeRepository.GetByCashAccountAsync` / `GetBySymbolAsync`），filter 期間
2. 對帳單 rows 與 trades 都跑 `IReconciliationMatcher.Key(row)` → `(date_bucket, abs_amount, sign)`
3. 雙向匹配：
   - 對帳單命中但 trade 沒有 → `Missing`（鼓勵建立 trade）
   - trade 命中但對帳單沒有 → `Extra`（鼓勵刪除或標記對帳單漏列）
   - 命中但金額差異 > 0.01 → `AmountMismatch`
4. 容忍度：日期 ± 1 天（銀行假日入帳）；金額 abs 值 ± 0.005（四捨五入）

### F3 對帳 UI

#### 入口
Import 頁面 TabControl 新增「對帳」分頁，與既有「匯入」「歷史」並列。

#### 工作台
- 上方工具列：選帳戶 ComboBox + 期間 DatePickerStart / DatePickerEnd + 「開始對帳」按鈕（彈 dialog 選資料來源：既有 batch 或上傳新檔）+ 「儲存簽收」按鈕（status → Resolved）
- 下方 DataGrid：依 Kind group（Missing 紅 / Extra 黃 / AmountMismatch 橘），每列三欄（日期 / 金額 / 對方）+ 動作：
  - Missing：「建立交易」（直接呼叫 `ImportRowMapper` + `ITradeRepository.AddAsync`）
  - Extra：「刪除交易」or「標記為對帳單漏列」
  - AmountMismatch：「以對帳單為準覆蓋」or「標記已解（手動修正）」
- 右側摘要 panel：總 diff 數 / 已解決數 / 帳戶餘額對帳結果（trade 累加 vs 對帳單期末餘額）

#### i18n keys (~12 組)
`Reconciliation.Tab` / `Reconciliation.New` / `Reconciliation.Period` / `Reconciliation.Account` / `Reconciliation.Diff.Missing` / `.Extra` / `.AmountMismatch` / `Reconciliation.Action.Create` / `.Delete` / `.MarkResolved` / `.OverwriteFromStatement` / `.Status.Open` / `.Resolved`

## 五、測試重點

| 層 | 重點 |
|---|---|
| Core | `ReconciliationDiff` 三類 kind 測試、Resolution 狀態機合法轉換 |
| DomainService | `DefaultReconciliationMatcher` key 計算（日期 bucket / sign / 容忍度） |
| Application | `ReconciliationService.CreateAsync` 大量 row vs 大量 trade 性能（500 vs 500）、空 statement / 空 trades 邊界、AmountMismatch 容忍度邊界 |
| Application | `ApplyResolutionAsync` 三類 kind × 五種 resolution 的合法組合（非法應拋例外） |
| Infrastructure | session + diff 連動 CRUD（FK cascade）、JSON 序列化 ImportPreviewRow snapshot |
| Tests count | 預估 +25–35 筆，總數應達 ~450 |

## 六、文件 / 收尾

- CHANGELOG v0.9.0 條目（F1/F2/F3 + D1）
- `docs/architecture/Bounded-Contexts.md` 加入 Reconciliation context（與 Importing 為鄰，共用 `ImportPreviewRow`）
- `docs/planning/Implementation-Roadmap.md` Phase 2 區塊把對帳從 P1 移到「已完成」
- 標 v0.9.0 tag
