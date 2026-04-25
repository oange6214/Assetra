# Assetra 實作任務拆解

## Phase 1：財務核心補齊

### 1. Budgeting 子系統
- 建立 `BudgetCategory`
- 建立 `BudgetPlan`
- 建立 `BudgetPeriod`
- 建立 `RecurringExpenseRule`
- 建立 `BudgetSqliteRepository`
- 新增 `BudgetPlanningService`
- 新增 `BudgetTrackingService`
- 新增 `RecurringTransactionWorkflowService`
- 製作預算頁與月結頁

### 2. Goals 子系統
- 建立 `FinancialGoal`
- 建立 `GoalMilestone`
- 建立 `GoalFundingRule`
- 建立 `GoalSqliteRepository`
- 新增 `GoalPlanningService`
- 新增 `GoalProgressQueryService`
- 新增 Goals 畫面

### 3. 淨資產趨勢
- 建立趨勢 query service
- 建立月 / 季 / 年切換
- 加入重大事件標註
- 製作趨勢圖與堆疊圖 UI

### 4. 匯入治理基礎
- 建立 `ImportBatch`
- 建立 `ImportRule`
- 建立 `ImportPreviewItem`
- 建立 `ImportConflict`
- 建立 CSV / Excel import preview flow
- 建立去重與確認提交流程

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
