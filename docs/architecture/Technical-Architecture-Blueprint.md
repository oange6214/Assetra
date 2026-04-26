# Assetra 技術架構藍圖

> 本文件描述的是 **中長期目標架構** 與建議演進方向，不代表所有模組都已經完整落地。
> 目前實作狀態請優先對照：
> - `docs/planning/Implementation-Roadmap.md`
> - `docs/planning/Next-Sprint-v0.6.0.md`
> - 根目錄 `README.md`

## 一、架構目標

Assetra 的技術架構目標是：

1. 支援資產、負債、交易、預算、目標、報表等多模組擴充
2. 保持 UI 與核心邏輯解耦
3. 讓資料來源、匯入、分析、報表都能逐步演進
4. 讓未來行動端、PWA、雲端同步有落地空間
5. 避免所有新功能都持續回灌到單一 ViewModel 或單一 Repository

---

## 二、分層架構

建議維持並強化以下分層：

```text
Assetra.Core
Assetra.Application
Assetra.Infrastructure
Assetra.WPF
Assetra.Tests
```

---

## 三、各層責任

## 1. Assetra.Core

### 角色
定義系統最核心的模型、規則與抽象介面。

### 應包含
- Entities
- Value Objects
- Enums
- Domain policies / rules
- Interfaces / contracts
- 純邏輯計算模型

### 典型內容
- `Trade`
- `AssetItem`
- `AppSettings`
- `TradeType`
- `LiabilitySubtype`
- `RecordActionType`
- `RecordTargetCategory`
- `ICurrencyService`
- `ITradeRepository`
- `IAssetRepository`

### 原則
- 不依賴 WPF
- 不依賴 SQLite
- 不依賴 HTTP / API
- 不依賴 UI 概念
- 不直接知道資料存在哪裡

---

## 2. Assetra.Application

### 角色
承接業務流程與使用案例（use cases / workflows）。

### 應包含
- Use cases
- Workflow services
- Query services
- DTOs / request / result
- Application-level orchestration
- 跨 repository / service 的流程協調

### 典型內容
- `PortfolioLoadService`
- `PortfolioSummaryService`
- `TransactionWorkflowService`
- `AddAssetWorkflowService`
- `CreditCardMutationWorkflowService`
- `CreditCardTransactionWorkflowService`
- `AccountMutationWorkflowService`
- `PositionDeletionWorkflowService`
- `TradeDeletionWorkflowService`

### 原則
- 可以依賴 `Core`
- 不應依賴 `WPF`
- 不應直接包含畫面控制邏輯
- 不做資料庫細節與 SQL
- 不做 XAML 狀態管理

---

## 3. Assetra.Infrastructure

### 角色
提供外部世界的實作，包括：
- SQLite persistence
- API / HTTP clients
- 市場資料來源
- CSV / Excel / PDF / OCR 匯入
- Background jobs / startup initializer

### 應包含
- Repository implementations
- HTTP service implementations
- Schema migrators
- File import/export implementations
- Update / persistence helpers

### 典型內容
- `AssetSqliteRepository`
- `TradeSqliteRepository`
- `PortfolioSqliteRepository`
- `CurrencyService`
- `TwseClient`
- `TpexClient`
- `FugleClient`
- `YahooFinanceHistoryProvider`
- `FinMindHistoryProvider`
- `StockScheduler`
- `AssetSchemaMigrator`
- `TradeSchemaMigrator`
- `PortfolioSchemaMigrator`

### 原則
- 可以依賴 `Core`
- 可以依賴 `Application` 的 contract
- 不依賴 `WPF`
- 不負責 UI 流程
- 不應直接組裝畫面狀態

---

## 4. Assetra.WPF

### 角色
提供桌面 UI、頁面、互動、表單狀態與使用者操作體驗。

### 應包含
- Views
- Controls
- Dialogs
- ViewModels
- Converters
- UI-only services
- Resource dictionaries / themes / localization

### 典型內容
- `MainWindow`
- `PortfolioView`
- `AlertsView`
- `SettingsView`
- `PortfolioViewModel`
- `TransactionDialogViewModel`
- `AddRecordDialog`
- `BuyTxForm`
- `CreditCardTxForm`
- `GlobalStyles.xaml`

### 原則
- 只呼叫 `Application` / `Core` contracts
- 不應直接碰 SQLite repository
- 不應直接寫入資料庫
- 不應承擔跨模組業務流程
- 負責 UI state 與操作感，但不負責核心規則

---

## 5. Assetra.Tests

### 角色
驗證各層邏輯、流程與 UI 行為。

### 分類建議
- `Core` 單元測試
- `Application` workflow / query service 測試
- `Infrastructure` repository / migration / provider 測試
- `WPF` ViewModel / flow / regression 測試

### 原則
- `Core` 優先測純邏輯
- `Application` 優先測使用案例與 guard condition
- `WPF` 只測 UI state 與 interaction，不重複測 domain logic

---

## 四、依賴方向

正確依賴方向應為：

```text
WPF -> Application -> Core
Infrastructure -> Core
Infrastructure -> Application (實作其 contract 時)
Tests -> all
```

### 規則
- `Core` 不依賴任何其他層
- `Application` 不依賴 `Infrastructure` concrete type
- `WPF` 不依賴 `Infrastructure` concrete repository
- `Infrastructure` 可以實作 `Core` / `Application` 所定義的介面
- `WPF` 應透過 DI 取得 `Application` service

---

## 五、核心技術切分

## 1. 記錄模型（Record Model）

目前應逐步從單一 `TradeType` 心智模型，演進為：

- `ActionType`
- `TargetCategory`
- `Subtype`

### 建議結構
- `RecordActionType`
  - Buy
  - Sell
  - Deposit
  - Withdrawal
  - Transfer
  - Borrow
  - Repay
  - Income
  - DividendCash
  - DividendStock
  - Charge
  - Refund

- `RecordTargetCategory`
  - Investment
  - CashAccount
  - Liability
  - CreditCard

- `LiabilitySubtype`
  - Loan
  - CreditCard

### 原則
UI 可以繼續用自然語意：
- 股票買入
- 信用卡消費
- 信用卡繳款

但系統內部應逐步走向更清楚的多維模型。

---

## 2. 資產模型（Asset Model）

建議將資產切成幾個主要群組：

- `Investment`
- `CashAccount`
- `Liability`
- `CreditCard`
- 未來可擴：
  - `Property`
  - `Insurance`
  - `RetirementAccount`
  - `Crypto`

### 負債建議
負債至少分為：
- `Loan`
- `CreditCard`

因為兩者欄位與行為差異很大：

#### Loan
- AnnualRate
- TermMonths
- StartDate
- PaymentSchedule

#### CreditCard
- BillingDay
- DueDay
- CreditLimit
- IssuerName
- OutstandingBalance

---

## 3. 收支與預算子系統（Budgeting）

這一塊建議作為獨立子系統，而不是只新增幾個交易類型。

### Core
- `BudgetCategory`
- `BudgetPlan`
- `BudgetPeriod`
- `BudgetRule`
- `RecurringExpenseRule`

### Application
- `BudgetPlanningService`
- `BudgetTrackingService`
- `RecurringTransactionWorkflowService`
- `BudgetSummaryQueryService`

### Infrastructure
- `BudgetSqliteRepository`
- recurring transaction scheduler
- import rule persistence

### WPF
- 預算頁
- 月結頁
- 分類規則管理 UI

---

## 4. 財務目標子系統（Goals）

> 現況：已落地的是 `Goals MVP`（資料模型、repository、WPF 頁面與基本進度呈現）。
> 下列 service 切分是建議中的完整形態，尚未全部在 `Application` 層獨立實作。

### Core
- `FinancialGoal`
- `GoalMilestone`
- `GoalFundingRule`

### Application
- `GoalPlanningService`
- `GoalProgressQueryService`
- `GoalFundingWorkflowService`

### Infrastructure
- `GoalSqliteRepository`
- 通知 persistence

### WPF
- Goals 頁
- 進度圖
- 里程碑提示

---

## 5. 淨資產與分析引擎

這塊建議逐步集中成共享分析服務。

### Core
- `NetWorthSnapshot`
- `AllocationSnapshot`
- `CashFlowSeries`
- `PerformanceSnapshot`

### Application
- `NetWorthTrendQueryService`
- `AllocationAnalysisService`
- `PerformanceAnalysisService`
- `RiskAnalysisService`

### 原則
不要讓：
- 淨資產趨勢
- 配置分析
- 投資績效
- 報表輸出

各自重算一套。  
應共享同一批 projection / calculation layer。

---

## 6. 風險與稅務引擎

### Core
- `DividendEvent`
- `TaxSummary`
- `RiskMetrics`
- `ConcentrationMetrics`

### Application
- `DividendCalendarService`
- `TaxSummaryService`
- `RiskMetricsService`
- `ConcentrationAlertService`

### 原則
先把計算引擎抽象化，再決定 UI 顯示形式。

---

## 六、資料治理架構

這是未來很關鍵的一層，建議獨立看待。

## 1. 匯入管線（Import Pipeline）

### Core
- `ImportSourceType`
- `ImportBatch`
- `ImportRule`
- `ImportPreviewItem`
- `ImportConflict`

### Application
- `ImportPreviewService`
- `ImportMappingService`
- `ImportDeduplicationService`
- `ImportCommitWorkflowService`
- `ReconciliationService`

### Infrastructure
- CSV parser
- Excel parser
- PDF parser
- OCR service adapter
- file watcher / file store

### WPF
- 匯入精靈
- 欄位映射 UI
- 衝突確認 UI
- 對帳介面

---

## 2. 匯入原則
匯入流程不應直接寫入交易資料表，而應經過：

1. 解析
2. 預覽
3. 去重
4. 衝突判斷
5. 使用者確認
6. 實際提交

這樣才能支援：
- 銀行帳單
- 信用卡帳單
- 券商報表
- OCR 輸入

---

## 七、報表與輸出架構

> 現況：目前已實作的是 `MonthEndReportService + ReportsView` 的月結報告 MVP。
> 下列資產負債表 / 現金流量表 / 損益表 / 匯出流程，仍屬目標架構。

### Core
- `ReportPeriod`
- `ReportSection`
- `StatementRow`
- `ExportFormat`

### Application
- `IncomeStatementService`
- `BalanceSheetService`
- `CashFlowStatementService`
- `ReportExportService`

### Infrastructure
- PDF export
- CSV export
- chart renderer
- snapshot serializer

### 原則
報表資料應由 application 組好，再交給 infrastructure 匯出。

---

## 八、更新與可靠性設計

## 1. 更新策略
建議維持目前較合理的雙模式：

### 正常啟動
- 先進主畫面
- 背景檢查更新
- 下載後提示重啟

### 啟動異常
- 保留 `startup.pending` marker
- 下次啟動先進入修復更新流程
- 若有新版則優先更新

### 原則
不要把所有更新檢查都塞在主流程前，但也不能讓壞版無法自救。

---

## 2. 資產管理（Assets Pipeline）
圖示與品牌資產建議固定如下：

```text
Assets/
├── svg/
├── png/
├── windows/
├── web/
├── package/
└── android/
```

### 用途
- `svg/`：設計 source
- `png/`：通用輸出
- `windows/`：Windows app icon
- `web/`：favicon
- `package/`：MSIX / tile / store 資產
- `android/`：Android 輸出

### 原則
不要複製多份同一個 icon 到根目錄。  
如果引用路徑不對，應修改專案引用與打包規則，而不是再複製一份。

---

## 九、未來平台擴展

## 1. 行動端 / PWA
若未來要做：
- iOS
- Android
- PWA

建議保留以下抽象：

- `Application` 不依賴 WPF
- 所有 workflow / query service 可被其他前端共用
- 匯出與圖表資料先回傳 DTO，而不是直接綁死 WPF 控件

## 2. 雲端同步
若未來加入同步，建議增加：
- sync metadata
- record versioning
- merge / conflict policy
- encrypted backup layer

---

## 十、技術債與設計原則

### 1. ViewModel 不應繼續長大
原則：
- UI 狀態放 ViewModel
- 業務流程進 Application
- 資料存取進 Infrastructure

### 2. 一致的 workflow 命名
建議統一：
- `*WorkflowService`：寫入流程
- `*QueryService`：查詢
- `*SummaryService`：彙總
- `*AnalysisService`：分析
- `*Migrator`：資料庫演進
- `*Initializer`：啟動初始化

### 3. 不要讓新功能直接繞回 repository
所有新功能都應優先先想：
- 這是 domain rule？
- application workflow？
- infrastructure implementation？
- UI state？

---

## 十一、推薦演進順序

### Phase 1
- 收支管理子系統
- 財務目標子系統
- 淨資產趨勢視覺化
- 資料治理 / 匯入管線基礎

### Phase 2
- 投資績效分析
- 外幣 / 美股 / FX 擴充
- 報表輸出
- 風險分析

### Phase 3
- 稅務模組
- OCR / PDF 匯入
- AI 財務助理
- 雲端同步

### Phase 4
- 不動產 / 保險 / 退休專戶
- PWA / 行動端
- 高階情境模擬與 FIRE 工具

---

## 十二、總結

Assetra 現在已具備：

- 投資
- 帳戶
- 負債
- 交易記錄
- 提醒
- 配置分析

下一步技術架構應該聚焦在：

1. 建立真正的子系統邊界
2. 建立共享的分析與報表計算層
3. 建立可擴充的匯入與資料治理架構
4. 保持 UI 與核心邏輯解耦

目標不是只加更多功能，而是讓 Assetra 能穩定成長為完整的個人財務平台。
