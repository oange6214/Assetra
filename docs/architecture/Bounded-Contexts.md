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

## 4. Goals Context  *(完整子系統 — v0.16.0)*

> Goals = 財務目標（買房頭期款、退休、旅遊基金）。**不要與 Recurring（訂閱/固定支出）混淆**。

### 責任
- 財務目標
- 里程碑
- 目標進度
- 資金來源規則

### 主要模型
- `FinancialGoal`（含 `TargetAmount` / `CurrentAmount` / `Deadline` / `Notes` 與衍生 `ProgressPercent` / `Remaining` / `DaysRemaining`）
- `GoalMilestone`（v0.16.0）
- `GoalFundingRule`（v0.16.0）

### 主要服務
- `IFinancialGoalRepository` / `GoalSqliteRepository`（含 `GoalSchemaMigrator`）
- `GoalPlanningService`（v0.16.0）
- `GoalProgressQueryService`（v0.16.0）
- `GoalsViewModel` / `GoalRowViewModel` 於 WPF 層提供 CRUD UI

---

## 5. Analysis Context

> 計畫 / 實作整合於下方第 8 節（v0.12.0 + v0.13.0 已落地）。本節保留 placeholder 以維持原 numbering；新功能（淨資產趨勢延伸、稅務）見 Roadmap Phase 3。

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
- 建立 session 的 UI 對話框 ✅（v0.10.0）
- Created / OverwrittenFromStatement 動作的 UI 路徑 ✅（v0.10.0，共用 `IImportRowApplier`）

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

## 9. Tax Context  *(完成 — v0.18.0)*

### 責任
- 股利所得、海外所得追蹤
- 二代健保補充保費計算
- 年度稅務摘要
- 報稅資料匯出（CSV / 申報軟體格式）

### 主要模型
- `TaxSummary`（含 `OverseasIncomeTotal` decimal 欄位、`DividendIncomeRecord` list；無獨立 `OverseasIncome` VO）
- `DividendIncomeRecord`

### 主要服務
- `TaxSummaryService`
- `TaxCalculationService`
- 配息 UI：`DividendCalendarViewModel`（WPF 層；無獨立 application-layer `DividendTrackingService`）

### 與其他 context 的關係
- 從 `Portfolio` 取得交易與配息資料
- 從 `Reporting` 取得期間報表數據做稅基計算

---

## 10. FX / Currency Context  *(完成 — v0.14.0 / v0.15.0)*

### 責任
- 多幣別帳戶換算
- FX 匯率查詢（靜態 + 可選 online）
- 美股 / ETF pipeline（`StockExchangeRegistry`，跨市場選股）

### 主要模型
- `Currency`（VO，內建常數 TWD / USD / JPY / HKD / EUR / CNY）
- `FxRate`（From / To / Rate / AsOfDate）

### 主要服務
- `IFxRateProvider` / `StaticFxRateProvider`
- `MultiCurrencyValuationService`
- `StockExchangeRegistry`

### 與其他 context 的關係
- `Portfolio`、`Analysis`、`Reporting` 所有計算透過此 context 換算為 base currency

---

## 11. Sync Context  *(GA — v0.20.0 → v0.21.0)*

### 責任
- 端到端加密雲端同步（AES-256-GCM，金鑰不上傳）
- 8 entity 類型 round-trip（Category / Trade / Asset / AssetGroup / AssetEvent / Portfolio / AutoCategorizationRule / RecurringTransaction）
- 衝突解決（LastWriteWins + manual conflict drain）
- soft delete tombstone 同步

### 主要模型
- `SyncEnvelope`
- `EntityVersion`
- `SyncResult`
- `SyncMetadata`

### 主要服務
- `SyncOrchestrator`（pull / resolve / push / save 主管線）
- `CompositeLocalChangeQueue`（8-key entity routing）
- `LastWriteWinsResolver`
- `EncryptingCloudSyncProvider`（AES-GCM wrapper）
- `HttpCloudSyncProvider`（wire format，見 Sync-Wire-Protocol.md）
- `SyncCoordinator`（WPF 層觸發入口，KDF + pipeline 組裝）

### 與其他 context 的關係
- 所有有 sync 欄位的 entity repo 實作 `ILocalChangeQueue` / `IXxxSyncStore`
- `Platform` 的 `AppSettings` 持有 deviceId / salt（KDF 材料）

---

## 12. Platform Context

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

## 13. Multi-Asset Context  *(規劃中 — v0.23.0 / v0.24.0)*

> 不動產、保險、退休專戶、實物資產四類資產，各有獨立生命週期與估值邏輯，不併入 Portfolio Context。

### 13a. RealEstate Context  *(v0.23.0)*

#### 責任
- 不動產持有、估值、貸款關聯
- 租金收入追蹤

#### 主要模型
- `RealEstate`（地址、類別、購入價、目前估值、估值方式、`MortgageLoanId?`）
- `RentalIncomeRecord`（月份、金額、帳戶）

#### 主要服務
- `RealEstateRepository`
- `RealEstateValuationService`

#### 與其他 Context 的關係
- → `Reporting`：BalanceSheet 資產端新增「不動產」行項；IncomeStatement 新增「租金收入」
- → `Analysis`：`ConcentrationAnalyzer` 納入不動產佔淨值比例
- → `Portfolio`（負債端）：關聯 `LiabilityItem`（房貸）

---

### 13b. Insurance Context  *(v0.23.0)*

#### 責任
- 保單生命週期（保費、到期日、保額）
- 壽險 / 儲蓄險現金價值追蹤

#### 主要模型
- `InsurancePolicy`（類別、年繳保費、保額、起保日、到期日、現金價值）
- `InsurancePremiumRecord`（年份、繳費金額、帳戶）

#### 主要服務
- `InsurancePolicyRepository`
- `InsuranceCashValueCalculator`

#### 與其他 Context 的關係
- → `Reporting`：BalanceSheet 資產端新增「保單現金價值」；費用端新增「保費支出」
- → `Tax`：保費是否可申報扣除額（台灣人身保險扣除額上限 24,000）
- → `Goals`：壽險保額可對應家庭財務目標

---

### 13c. RetirementAccount Context  *(v0.24.0)*

#### 責任
- 勞退自提 / 雇提 / 勞保年資
- 提領規則與提前提領罰金
- 退休目標連動

#### 主要模型
- `RetirementAccount`（類別：勞退 / IRA / 401K、餘額、累計年資、法定提領年齡）
- `RetirementContribution`（年份、自提金額、雇提金額）

#### 主要服務
- `RetirementAccountRepository`
- `RetirementProjectionService`（預估退休時餘額）

#### 與其他 Context 的關係
- → `Reporting`：BalanceSheet 資產端新增「退休專戶」
- → `Goals`：退休目標可直接讀取退休專戶進度
- → `Analysis`：`FireCalculator`（v0.25.0）讀取退休專戶餘額

---

### 13d. PhysicalAsset Context  *(v0.24.0)*

#### 責任
- 黃金、藝術品、收藏品等實物資產持有與估值

#### 主要模型
- `PhysicalAsset`（名稱、類別、購入成本、目前估值、估值方式、取得日）

#### 主要服務
- `PhysicalAssetRepository`
- `PhysicalAssetValuationService`

#### 與其他 Context 的關係
- → `Reporting`：BalanceSheet 資產端新增「實物資產」
- → `Analysis`：集中度分析納入實物資產佔比

---

## 14. Simulation Context  *(規劃中 — v0.25.0 / v0.26.0)*

> 純計算層，無持久化。所有輸入來自現有 Portfolio / Budgeting / Analysis / Multi-Asset context 的查詢服務。

### 14a. FIRE Context  *(v0.25.0)*

#### 責任
- FIRE（Financial Independence, Retire Early）達成年數試算
- 4% 法則 / SWR（Safe Withdrawal Rate）可持續性模擬
- 退休後每年提領是否撐得過 30 年

#### 主要模型
- `FireScenario`（目前淨資產、月儲蓄、預期報酬率、目標年支出、通膨率）
- `FireResult`（FIRE 達成年數、達成時資產、可持續機率）

#### 主要服務
- `FireCalculator`（確定性試算）
- `SustainabilityAnalyzer`（給定退休資產，模擬提領可持續性）

#### 與其他 Context 的關係
- 讀取 `Portfolio`、`Budgeting`、`RetirementAccount`（輸入）
- → `Goals`：FIRE 達成日可作為退休 Goal 的預測到期日
- 無持久化：每次計算即棄

---

### 14b. Monte Carlo Context  *(v0.26.0)*

#### 責任
- 利率 / 通膨 / 薪資成長率 stochastic 模擬
- 1000+ 次模擬 → 成功率分布 + fan chart 資料
- 淨資產 30 年走勢的 10/50/90 百分位

#### 主要模型
- `MonteCarloScenario`（模擬次數、變數設定：利率分布、通膨分布）
- `MonteCarloResult`（percentile 序列、成功率、破產年份分布）

#### 主要服務
- `MonteCarloSimulator`
- `StochasticRateProvider`（常態分布 / 歷史抽樣）

#### 與其他 Context 的關係
- 讀取 `FireScenario`（或直接讀 Portfolio / Budgeting）
- → `Reporting`：可選匯出模擬圖表（PNG/PDF）
- 無持久化

---

## 三、Context 關係

```text
Portfolio ----> Analysis
Portfolio ----> Reporting
Portfolio ----> Budgeting
Portfolio ----> Goals
Portfolio ----> Tax
Portfolio ----> FX
Recurring ----> Budgeting
Recurring ----> Portfolio
Importing ----> Portfolio
Importing ----> Budgeting
Reconciliation ----> Importing
Reconciliation ----> Portfolio
Tax       ----> Portfolio
Tax       ----> Reporting
FX        ----> Portfolio
FX        ----> Analysis
FX        ----> Reporting
Sync      ----> all entity repos
Platform  ----> all
RealEstate    ----> Reporting
RealEstate    ----> Analysis
Insurance     ----> Reporting
Insurance     ----> Tax
Retirement    ----> Reporting
Retirement    ----> Goals
Retirement    ----> Analysis (FIRE)
PhysicalAsset ----> Reporting
PhysicalAsset ----> Analysis
FIRE          ----> Portfolio
FIRE          ----> Budgeting
FIRE          ----> Retirement
FIRE          ----> Goals
MonteCarlo    ----> FIRE
MonteCarlo    ----> Reporting
```

### 說明
- `Portfolio` 是目前主核心
- `Analysis` 依賴 Portfolio 的資料做計算
- `Reporting` 依賴 Portfolio 與 Analysis 的結果
- `Budgeting` 依賴交易資料，但不是 Portfolio 的子畫面而已
- `Goals` 依賴帳戶、投資、預算資料做進度追蹤
- `Importing` 是進資料的入口，不應直接寫進 UI 狀態
- `Multi-Asset`（RealEstate / Insurance / Retirement / Physical）各自獨立，共同匯入 Reporting
- `Simulation`（FIRE / Monte Carlo）為純計算層，讀取所有 context，無持久化

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
5. Multi-Asset entities 一律加 `EntityVersion` 欄位（sync 準備）
6. Simulation Context 保持純計算，不持久化模擬結果

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
- `Tax`
- `FX / Currency`
- `Sync`
- `Platform`
- `RealEstate`（v0.23.0）
- `Insurance`（v0.23.0）
- `RetirementAccount`（v0.24.0）
- `PhysicalAsset`（v0.24.0）
- `Simulation / FIRE`（v0.25.0）
- `Simulation / MonteCarlo`（v0.26.0）

這樣才能在功能變多時，仍然保持結構穩定、測試清楚、UI 不失控。
