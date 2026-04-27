# Assetra Bounded Contexts

## 一、目的

這份文件用來定義 Assetra 未來可拆分的 bounded contexts，避免新功能全部繼續塞進單一 `Portfolio` 心智模型。

---

## 二、建議的 Context 劃分

## 1. Portfolio Context

### 責任
- 投資部位
- 現金帳戶
- 負債
- 信用卡
- 交易記錄
- 配置分析
- 再平衡

### 主要模型
- `Trade`
- `AssetItem`
- `PortfolioEntry`
- `PositionSnapshot`
- `LiabilitySubtype`

### 主要服務
- `PortfolioLoadService`
- `PortfolioSummaryService`
- `TransactionWorkflowService`
- `CreditCardTransactionWorkflowService`

---

## 2. Budgeting Context  *(已實作大部分)*

### 責任
- 預算分類
- 月度預算
- 月結
- 預算差異追蹤

### 主要模型（現況）
- `ExpenseCategory`（對應原規劃的 `BudgetCategory`）
- `Budget` / `BudgetTemplate`（對應原規劃的 `BudgetPlan`）

### 主要服務（現況）
- `MonthlyBudgetSummaryService`
- `BudgetSqliteRepository`

### 與 Portfolio 的關係
- 讀取交易資料做分類與月結
- 不直接擁有投資部位與負債邏輯

---

## 3. Recurring Context  *(已實作)*

> 注意：先前文件把 Recurring（訂閱/固定支出週期）與 Goals（財務目標）混為一談。它們是兩個不同的 context，這裡分開定義。

### 責任
- 訂閱與固定支出週期管理
- 自動產生交易（每月房租、水電、保險、訂閱費）
- 待確認交易的審核與入帳

### 主要模型（現況）
- `RecurringTransaction`
- `PendingRecurringEntry`

### 主要服務（現況）
- `RecurringTransactionScheduler`
- `RecurringViewModel` / `RecurringView`

### 與 Budgeting 的關係
- 自動產生的交易會被 Budgeting 計入月結
- Recurring 不擁有預算邏輯，只負責產生與審核交易

---

## 4. Goals Context  *(已實作 MVP — v0.6.0)*

> Goals = 財務目標（買房頭期款、退休、旅遊基金）。**不要與 Recurring（訂閱/固定支出）混淆**。

### 責任
- 財務目標
- 里程碑（規劃中）
- 目標進度
- 資金來源規則（規劃中）

### 主要模型（現況）
- `FinancialGoal`（v0.6.0 已實作；含 `TargetAmount` / `CurrentAmount` / `Deadline` / `Notes` 與衍生 `ProgressPercent` / `Remaining` / `DaysRemaining`）
- `GoalMilestone`（規劃中）
- `GoalFundingRule`（規劃中）

### 主要服務（現況）
- `IFinancialGoalRepository` / `GoalSqliteRepository`（v0.6.0 已實作；含 `GoalSchemaMigrator`）
- `GoalsViewModel` / `GoalRowViewModel` 於 WPF 層提供 CRUD UI
- `GoalPlanningService`（規劃中）
- `GoalProgressQueryService`（規劃中）
- `GoalFundingWorkflowService`（規劃中）

---

## 5. Analysis Context

### 責任
- 淨資產趨勢
- 投資績效
- 風險分析
- 報表計算

### 主要模型
- `NetWorthSnapshot`
- `PerformanceSnapshot`
- `RiskMetrics`
- `TaxSummary`

### 主要服務
- `NetWorthTrendQueryService`
- `PerformanceAnalysisService`
- `RiskAnalysisService`
- `TaxSummaryService`

### 原則
- 主要提供計算與 projection
- 不直接處理 UI 或資料庫細節

---

## 6. Importing Context  *(MVP 完成 — v0.7.0；對帳分離為獨立 context — v0.9.0；OCR 延後)*

> 對應實作位於 `Assetra.Application/Import/`、`Assetra.Infrastructure/Import/`、`Assetra.WPF/Features/Import/`。

### 責任
- 檔案匯入（CSV / Excel；Top 5 台股銀行 / 券商）✅
- 欄位映射（宣告式 `CsvParserConfigs` / `ExcelParserConfigs`，取代原本規劃的 service）✅
- 去重（`ImportConflictDetector` + `ImportMatchKey`）✅
- 衝突確認（每列 Skip / Overwrite / Add anyway 處置）✅
- 對帳 ✅（v0.9.0 拆為獨立 Reconciliation Context，見下）

### 主要模型
- `ImportBatch` ✅
- `ImportPreviewRow` ✅（取代原規劃的 `ImportPreviewItem`）
- `ImportConflict` ✅
- `AutoCategorizationRule` ✅ v0.8（手動 + 匯入共用，含 `MatchField` / `MatchType` / `AppliesTo`，取代原規劃的 `ImportRule`）
- `ImportBatchHistory` ✅ v0.8（rollback 用 entries + JSON snapshot）

### 主要服務
- `ImportConflictDetector` ✅（取代原規劃的 `ImportDeduplicationService`）
- `ImportApplyService` ✅（取代原規劃的 `ImportCommitWorkflowService`）
- `ImportFormatDetector` + `ImportParserFactory` ✅（取代原規劃的 `ImportPreviewService` / `ImportMappingService`）

---

## 6b. Reconciliation Context  *(MVP 完成 — v0.9.0)*

> 對應實作位於 `Assetra.Core/Models/Reconciliation/`、`Assetra.Application/Reconciliation/`、`Assetra.Infrastructure/Persistence/Reconciliation*`、`Assetra.WPF/Features/Reconciliation/`。

### 責任

- 將對帳單預覽列（沿用 `ImportPreviewRow`）與已落地的 `Trade` 雙向配對 ✅
- 產出 Missing / Extra / AmountMismatch 三類差異供逐筆裁決 ✅
- 維持 Kind × Resolution 合法狀態機，避免錯置裁決 ✅
- Session SignOff 後鎖定 ✅
- 建立 session 的 UI 對話框 ⏳（v0.10+）
- Created / OverwrittenFromStatement 動作的 UI 路徑 ⏳（待 `ImportRowMapper` 整合）

### 主要模型

- `ReconciliationSession`（帳戶 + 期間 + 來源批次 + 狀態 Open / Resolved / Archived）
- `ReconciliationDiff`（Kind = Missing / Extra / AmountMismatch；Resolution = Pending / Created / Deleted / MarkedResolved / Ignored / OverwrittenFromStatement）
- 共用 `ImportPreviewRow`（v0.9 起跨 Importing / Reconciliation）

### 主要服務

- `IReconciliationMatcher` / `DefaultReconciliationMatcher`（日期 ±1 天、金額容忍 0.005，sign-aware）
- `IReconciliationService` / `ReconciliationService`（`CreateAsync` / `RecomputeAsync` / `ApplyResolutionAsync` / `SignOffAsync`，並暴露 `ComputeDiffs` / `EnsureLegalTransition` 兩個 pure static 方法供測試與外部裁決驗證）
- `IReconciliationSessionRepository` / `ReconciliationSessionSqliteRepository`

### 與其他 context 的關係

- 從 `Importing` 取得 `ImportPreviewRow`（直接、或透過 `ImportBatchHistory.GetPreviewRowsAsync`）
- 對 `Portfolio` 的 `Trade` 做配對；Resolution = Deleted 時呼叫 `ITradeRepository.RemoveAsync`
- Created / OverwrittenFromStatement 應由上層整合 `ImportRowMapper` 寫入 trade 後再回呼 `ApplyResolutionAsync` 標記狀態

---

## 7. Reporting Context  *(MVP 完成)*

> v0.11.0 完成三大報表（Income / Balance Sheet / Cash Flow）+ PDF (QuestPDF Community) / CSV 匯出，Trade journal 為單一事實源。


### 責任
- 財務報表
- PDF / CSV 匯出
- 圖表輸出

### 主要模型
- `ReportPeriod`
- `ReportSection`
- `StatementRow`
- `ExportFormat`

### 主要服務
- `IncomeStatementService`
- `BalanceSheetService`
- `CashFlowStatementService`
- `ReportExportService`

---

## 8. Analysis Context  *(MVP 完成 — v0.12.0；Risk 擴充 — v0.13.0)*

> 投資績效與風險分析。XIRR / TWR / MWR / benchmark / P&L attribution + 年化波動率 / 最大回撤 / Sharpe / HHI，輸出至 Reports 頁的 Performance 與 Risk 區塊。

### 責任
- 投資績效計算
- benchmark 對比
- 損益歸因
- 風險指標（波動率、回撤、Sharpe）
- 持股集中度（HHI、Top-N、警示 flag）

### 主要模型
- `CashFlow`
- `PerformancePeriod`
- `PerformanceResult`
- `AttributionBucket`
- `DrawdownPoint`
- `ConcentrationBucket`
- `RiskMetrics`

### 主要服務
- `XirrCalculator`
- `TimeWeightedReturnCalculator`
- `MoneyWeightedReturnCalculator`
- `BenchmarkComparisonService`
- `PnlAttributionService`
- `VolatilityCalculator`
- `DrawdownCalculator`
- `SharpeRatioCalculator`
- `ConcentrationAnalyzer`

---

## 9. Platform Context

### 責任
- 設定
- 更新
- 備份還原
- 同步
- 通知
- 品牌資產與應用程式層設定

### 主要模型
- `AppSettings`
- `SyncMetadata`
- `BackupManifest`

### 主要服務
- `AppSettingsService`
- `UpdateService`
- `BackupService`
- `SyncService`
- `NotificationService`

---

## 三、Context 關係

```text
Portfolio ----> Analysis
Portfolio ----> Reporting
Portfolio ----> Budgeting
Portfolio ----> Goals
Recurring ----> Budgeting
Recurring ----> Portfolio
Importing ----> Portfolio
Importing ----> Budgeting
Reconciliation ----> Importing
Reconciliation ----> Portfolio
Platform  ----> all
```

### 說明
- `Portfolio` 是目前主核心
- `Analysis` 依賴 Portfolio 的資料做計算
- `Reporting` 依賴 Portfolio 與 Analysis 的結果
- `Budgeting` 依賴交易資料，但不是 Portfolio 的子畫面而已
- `Goals` 依賴帳戶、投資、預算資料做進度追蹤
- `Importing` 是進資料的入口，不應直接寫進 UI 狀態

---

## 四、實作建議

### 短期
- 維持 monolith 專案結構
- 先在 `Application` 層按 context 分資料夾
- 先建立 service / DTO / workflow 邊界

### 中期
- 將 repository / service / docs / tests 按 context 分組
- 明確建立 context 對應的測試集

### 長期
- 若功能繼續成長，可考慮拆成多專案：
  - `Assetra.Budgeting`
  - `Assetra.Analysis`
  - `Assetra.Reporting`

---

## 五、原則

1. 新功能先決定屬於哪個 context，再開始寫
2. 不要讓 `PortfolioViewModel` 繼續吸收所有新需求
3. 分析、報表、匯入都應是獨立 context，不是工具類別集合
4. `Platform` 只負責應用程式層能力，不承擔財務業務邏輯

---

## 六、總結

Assetra 未來最值得的演進方向，不是只持續往 `Portfolio` 疊功能，而是逐步形成：

- `Portfolio`
- `Budgeting`
- `Recurring`
- `Goals`
- `Analysis`
- `Importing`
- `Reconciliation`
- `Reporting`
- `Platform`

這樣才能在功能變多時，仍然保持結構穩定、測試清楚、UI 不失控。
