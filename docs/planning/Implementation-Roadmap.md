# Assetra 實作任務拆解

## Phase 1：財務核心補齊

### 1. Budgeting 子系統  *(已完成 ~95%，仍缺匯入治理與對帳流程)*
- [x] 建立 `ExpenseCategory`（取代原規劃的 `BudgetCategory`）
- [x] 建立 `Budget` / `BudgetTemplate`（取代原規劃的 `BudgetPlan`）
- [x] 建立 `BudgetSqliteRepository`
- [x] 新增 `MonthlyBudgetSummaryService`（合併原規劃的 `BudgetPlanningService` + `BudgetTrackingService`）
- [x] 製作預算頁（`CategoriesView`）
- [x] 製作月結報告 MVP UI（v0.6.0：`Features/Reports/ReportsView` + `ReportsViewModel`）

### 2. Recurring 子系統  *(已完成)*

> 注意：先前文件把這項放在 Budgeting 之下並命名為「`RecurringExpenseRule` / `RecurringTransactionWorkflowService`」；實際以獨立 context 完成，與 Goals 是不同概念。

- [x] 建立 `RecurringTransaction`
- [x] 建立 `PendingRecurringEntry`
- [x] 建立 `RecurringTransactionScheduler`
- [x] 製作 `RecurringView` / `RecurringViewModel`

### 3. Goals 子系統  *(MVP 完成 — v0.6.0；完整子系統未完成)*

> Goals = 財務目標（買房頭期款、退休、旅遊基金）。**不要與已完成的 Recurring 混淆**。

- [x] 建立 `FinancialGoal`
- [ ] 建立 `GoalMilestone`（v0.7+）
- [ ] 建立 `GoalFundingRule`（v0.7+）
- [x] 建立 `GoalSqliteRepository`（含 `GoalSchemaMigrator`）
- [ ] 新增 `GoalPlanningService`（v0.7+）
- [ ] 新增 `GoalProgressQueryService`（v0.7+；目前進度由 `FinancialGoal` 衍生屬性 + ViewModel 計算）
- [x] 新增 Goals 畫面（`Features/Goals/GoalsView` + `GoalsViewModel`）

### 4. 淨資產趨勢  *(MVP 完成 — v0.6.0；堆疊圖、事件標註與更完整分析延後)*
- [x] `PortfolioDailySnapshot` / `PositionSnapshot` 資料層已備
- [x] `FinancialOverviewViewModel` 計算 `TotalNetWorth`
- [x] `DashboardViewModel` / `DashboardTabPanel` 基本 UI
- [x] 建立趨勢 query service（沿用 `IPortfolioHistoryQueryService`，由 `PortfolioHistoryViewModel` 篩選）
- [x] 建立 30 / 90 / 180 / 365 / All 切換 + 自訂日期範圍（`DateRangePicker`）
- [ ] 加入重大事件標註（v0.7+）
- [x] 製作趨勢圖（`Features/Trends/TrendsView`，LiveCharts 折線圖）
- [ ] 堆疊圖 UI（v0.7+：依資產類別堆疊，需 `PortfolioDailySnapshot` schema 擴充）

### 5. 匯入治理基礎  *(v0.7.0 完成 MVP；v0.8.0 完成 Phase 2：統一規則 + batch history + rollback)*
- [x] 建立 `ImportBatch`
- [x] 建立 `ImportConflict`
- [x] 建立 `ImportPreviewRow`
- [x] 建立 `ImportSourceKind` / `ImportFileType` / `ImportFormat`
- [x] 建立 `IImportFormatDetector` / `IImportParser`
- [x] 建立 CSV / Excel import preview flow（`ConfigurableCsvParser` / `ConfigurableExcelParser` 由 `CsvParserConfigs` / `ExcelParserConfigs` 驅動）
- [x] 建立去重與確認提交流程（`ImportConflictDetector` + `ImportApplyService` + `Features/Import` UI）
- [x] 統一手動 + 匯入自動分類規則（v0.8.0：擴充 `AutoCategorizationRule` 加上 `MatchField` / `MatchType` / `AppliesTo`，取代原規劃的獨立 `ImportRule`）
- [x] 匯入歷史 + rollback（v0.8.0：`ImportBatchHistory` + `ImportRollbackService`）

### 6. Reconciliation 子系統  *(MVP 完成 — v0.9.0)*

- [x] 建立 `ReconciliationSession` / `ReconciliationDiff`（含 Status / Kind / Resolution enum）
- [x] 建立 `IReconciliationSessionRepository` + `ReconciliationSessionSqliteRepository`（含 `ReconciliationSchemaMigrator`、statement_rows_json 欄）
- [x] 建立 `IReconciliationMatcher` + `DefaultReconciliationMatcher`（日期 ±1 天、金額容忍 0.005、sign-aware）
- [x] 建立 `IReconciliationService` + `ReconciliationService`（`ComputeDiffs` / `EnsureLegalTransition` 暴露為 public static 供測試）
- [x] `ImportBatchEntry` 加 `PreviewRowJson` 並由 `ImportApplyService` 寫入；新增 `IImportBatchHistoryRepository.GetPreviewRowsAsync`
- [x] Reconciliation Tab UI（Import 頁 TabControl；MVP Actions = Mark Resolved / Ignore / Delete Trade）
- [ ] 「新建 Session」對話框（v0.10+）
- [ ] Created / OverwrittenFromStatement 在 UI 上的執行路徑（待 `ImportRowMapper` 整合，v0.10+）
- [ ] DataGrid Kind 分組與即時餘額對帳面板（v0.10+）

## Phase 2：投資分析與專業化

### 1. 投資績效分析
- 建立 XIRR 計算器
- 建立 TWR / MWR 計算器
- 建立 benchmark 對比 service
- 建立損益歸因模型

### 2. 報表系統
- 建立資產負債表 service
- 建立現金流量表 service
- 建立損益表 service
- 建立 PDF / CSV export

### 3. 外幣與美股
- 建立外幣帳戶模型
- 建立 FX 換算策略
- 擴充美股 / ETF quote & history pipeline
- 擴充投資 UI 與交易流程

### 4. 風險分析
- 建立波動度 / 最大回撤 / Sharpe Ratio 計算
- 建立集中度分析
- 加入集中度警示

## Phase 3：自動化與治理進階

### 1. 稅務模組
- 建立 `TaxSummary`
- 建立股利所得追蹤
- 建立海外所得追蹤
- 加入報稅匯出

### 2. 進階匯入
- 銀行帳單匯入
- 信用卡帳單匯入
- PDF parser
- OCR adapter

### 3. AI 財務助理
- 先做自然語言查詢
- 再做摘要與提醒建議
- 最後才做規劃建議

### 4. 雲端同步
- 設計 sync metadata
- 設計 merge / conflict policy
- 建立加密同步層

## Phase 4：差異化與平台擴展

### 1. 多元資產
- 不動產
- 保險
- 退休專戶
- 實物資產

### 2. 情境模擬
- FIRE 計算機
- 退休提領模擬
- 利率 / 通膨 / 薪資變動模擬

### 3. 多端體驗
- PWA
- 行動端
- 推播通知

## 橫向工程任務

### 架構
- 所有新功能優先進 `Application`
- 避免 WPF 直接碰 repository
- 共用計算邏輯集中到 analysis / summary 層

### 測試
- 每個新子系統建立 workflow tests
- 匯入流程加 conflict / dedupe tests
- 分析引擎加 calculation tests
- UI 只測 state 與 interaction

### 文件
- 更新 architecture docs
- 更新 module map
- 更新 feature roadmap
