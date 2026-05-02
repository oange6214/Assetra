# Assetra 技術架構藍圖

> 本文件描述的是 **中長期目標架構** 與建議演進方向，不代表所有模組都已經完整落地。
> 目前實作狀態請優先對照：
> - `docs/planning/Implementation-Roadmap.md`
> - `docs/releases/CHANGELOG.md`
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
- `ExpenseCategory`
- `Budget`
- `BudgetTemplate`
- `BudgetPeriod`
- `AutoCategorizationRule`
- `RecurringTransaction`

### Application
- `MonthlyBudgetSummaryService`
- `RecurringTransactionScheduler`

### Infrastructure
- `BudgetSqliteRepository`
- recurring transaction persistence
- category / auto-categorization persistence

### WPF
- 預算頁
- 月結頁
- 分類規則管理 UI

---

## 4. 財務目標子系統（Goals）

> 現況（v0.16.0）：Goals 完整子系統已落地，含 `GoalMilestone` / `GoalFundingRule` / `GoalPlanningService` / `GoalProgressQueryService`。下列模型與 service 均已實作。

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

> 現況（v0.19.0）：匯入管線完整落地。CSV / Excel / PDF / OCR 全鏈路可用；`AutoCategorizationRule`（含 MatchField / MatchType / AppliesTo）、`ImportBatchHistory` + rollback、Reconciliation 對帳工作台（v0.9–v0.10）均已出貨。

### Core
- `ImportSourceKind` / `ImportFileType` / `ImportFormat` ✅
- `ImportBatch` ✅
- `ImportPreviewRow` ✅
- `ImportConflict` / `ImportConflictResolution` ✅
- `ImportApplyOptions` / `ImportApplyResult` ✅
- `AutoCategorizationRule` ✅（取代原規劃的 `ImportRule`，統一手動交易與匯入分類規則）

### Application
- `ImportConflictDetector` ✅（取代原規劃的 `ImportDeduplicationService`）
- `ImportApplyService` ✅（取代原規劃的 `ImportCommitWorkflowService`）
- `ImportMatchKey` ✅
- `ImportPreviewService` / `ImportMappingService` ⏳（目前由 ViewModel + parser config 直接驅動，預覽流程不需要獨立 service）
- `ReconciliationService` ⏳

### Infrastructure
- `IImportFormatDetector` 實作（`ImportFormatDetector`）✅
- `IImportParser` 實作（`ConfigurableCsvParser` / `ConfigurableExcelParser`）✅
- CSV parser ✅（CsvHelper 33.0.1）
- Excel parser ✅（ClosedXML 0.105.0）
- PDF parser ✅（`PdfImportParser` + `PdfPigStatementParser`；仍需擴充更多銀行 / 券商模板）
- OCR service adapter ✅（`TesseractOcrAdapter`；仍需改善使用者設定與格式辨識覆蓋率）
- file watcher / file store ⏳

### WPF
- `Features/Import` 主流程 UI ✅（drop zone + 偵測 chip + 預覽 grid + Resolution 下拉 + 現金帳戶選擇 + Apply）
- 欄位映射 UI ⏳（v0.7 採宣告式 config 取代）
- 衝突確認 UI ✅（每列 Resolution 下拉）
- 對帳介面 ⏳

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

> 現況（v0.11.0）：三大報表（損益表 / 資產負債表 / 現金流量表）與 PDF / CSV 匯出（`ReportExportService`）均已落地。

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

### Phase 1 ✅（v0.6–v0.10）
- 收支管理子系統
- 財務目標子系統
- 淨資產趨勢視覺化
- 資料治理 / 匯入管線基礎、對帳工作台

### Phase 2 ✅（v0.11–v0.15）
- 投資績效分析
- 外幣 / 美股 / FX 擴充
- 報表輸出
- 風險分析

### Phase 3 ✅（v0.16–v0.21）
- Goals 完整子系統（v0.16）
- 趨勢圖增強：事件標註 + 堆疊圖（v0.17）
- 稅務模組（v0.18）
- OCR / PDF 匯入（v0.19）
- 雲端同步 GA（v0.20–v0.21）

### Phase 4（多元資產 / 情境模擬已 ship — v0.22.0）
- ✅ 不動產 / 保險 / 退休專戶 / 實物資產（v0.22.0）
- ✅ 高階情境模擬：FIRE / Monte Carlo（v0.22.0）
- AI 財務助理（待規劃，v0.23.0+）
- PWA / 行動端（v0.24.0+ / v0.25.0+）

---

## 十三、多元資產架構（✅ v0.22.0）

### 1. Folder 結構

```
Assetra.Core/Models/MultiAsset/
  RealEstate.cs
  RentalIncomeRecord.cs
  InsurancePolicy.cs
  InsurancePremiumRecord.cs
  RetirementAccount.cs
  RetirementContribution.cs
  PhysicalAsset.cs

Assetra.Core/Interfaces/MultiAsset/
  IRealEstateRepository.cs
  IInsurancePolicyRepository.cs
  IRetirementAccountRepository.cs
  IPhysicalAssetRepository.cs

Assetra.Application/MultiAsset/
  RealEstateValuationService.cs
  InsuranceCashValueCalculator.cs
  RetirementProjectionService.cs
  PhysicalAssetValuationService.cs

Assetra.Infrastructure/Persistence/MultiAsset/
  RealEstateSqliteRepository.cs
  InsurancePolicySqliteRepository.cs
  RetirementAccountSqliteRepository.cs
  PhysicalAssetSqliteRepository.cs
```

### 2. Entity 版本規範

所有多元資產 entity 一律包含 `EntityVersion`，與現有 sync 架構對齊：

```csharp
record RealEstate(
    Guid Id,
    string Name,
    // ... 業務欄位 ...
    EntityVersion Version   // sync 所需
);
```

### 3. BalanceSheet 擴充

`BalanceSheetService` 新增資產聚合：

```
資產端
  ├─ 投資部位（既有）
  ├─ 現金帳戶（既有）
  ├─ 不動產淨值（RealEstate.CurrentValue - 房貸餘額）← 新增
  ├─ 保單現金價值（InsurancePolicy.CashValue）← 新增
  ├─ 退休專戶（RetirementAccount.Balance）← 新增
  └─ 實物資產（PhysicalAsset.CurrentValue）← 新增

負債端
  └─ 貸款 / 信用卡（既有）
```

### 4. Reporting 新增行項

| 報表 | 新增行項 | 來源 |
|---|---|---|
| BalanceSheet | 不動產淨值、保單現金價值、退休專戶、實物資產 | MultiAsset repos |
| IncomeStatement | 租金收入、保費支出 | RentalIncomeRecord、InsurancePremiumRecord |

### 5. Sync 整合

v0.22.0 已加的多元資產 entity 在 `CompositeLocalChangeQueue` 的 routing key：

```csharp
// 新增 4 個 entity type
"RealEstate"         → IRealEstateRepository
"InsurancePolicy"    → IInsurancePolicyRepository
"RetirementAccount"  → IRetirementAccountRepository
"PhysicalAsset"      → IPhysicalAssetRepository
```

---

## 十四、情境模擬架構（✅ v0.22.0）

### 1. 設計原則：純計算層

Simulation context **不持久化**。所有計算結果為 transient：

```
輸入（來自 UI）
  → FireScenario / MonteCarloScenario（record，純資料）
  → FireCalculator / MonteCarloSimulator（stateless service）
  → FireResult / MonteCarloResult（回傳給 UI）
  → UI 顯示圖表（不寫入 DB）
```

### 2. Folder 結構

```
Assetra.Core/Models/Simulation/
  FireScenario.cs
  FireResult.cs
  WithdrawalProjection.cs
  MonteCarloScenario.cs
  MonteCarloResult.cs
  RateDistribution.cs

Assetra.Application/Simulation/
  FireCalculator.cs
  SustainabilityAnalyzer.cs
  MonteCarloSimulator.cs
  StochasticRateProvider.cs
```

### 3. Monte Carlo 亂數策略

使用 `Random.Shared` + Box-Muller 轉換產生常態分布樣本，**不引入外部統計套件**：

```csharp
static double NextGaussian(double mean, double stdDev)
{
    double u1 = 1.0 - Random.Shared.NextDouble();
    double u2 = 1.0 - Random.Shared.NextDouble();
    double z  = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
    return mean + stdDev * z;
}
```

### 4. Fan Chart 視覺化

使用現有 `LiveChartsCore.SkiaSharpView.WPF`（已有）：
- 三條 `LineSeries`：P10（下緣）/ P50（中線）/ P90（上緣）
- `AreaSeries` 填滿 P10–P90 區間（半透明）
- X 軸：年份；Y 軸：資產餘額（base currency）

---

## 十二、總結

Assetra 截至 v0.21.3 已具備：

- 投資 / 帳戶 / 負債 / 交易記錄 / 提醒 / 配置分析
- 收支 / 預算 / Recurring / Goals 完整子系統
- 匯入治理全鏈路（CSV / Excel / PDF / OCR + 對帳 + rollback）
- 三大財務報表 + PDF / CSV 匯出
- 投資績效（XIRR / TWR / MWR / benchmark / PnL）+ 風險分析
- 外幣 / 美股 / FX 換算
- 稅務模組
- 雲端同步（AES-GCM 端到端加密，8 entity GA）

Phase 4 架構重點：

1. ✅ **多元資產模型**（v0.22.0）— 獨立 MultiAsset context，BalanceSheet / Reporting 接入，全 entity 加 EntityVersion
2. ✅ **情境模擬計算引擎**（v0.22.0）— 純計算層，無 persistence，Box-Muller Monte Carlo，LiveChartsCore fan chart
3. **LLM adapter 整合**（AI 財務助理，v0.23.0+）— query intent routing 不穿透 domain layer
4. **Web API 抽出**（v0.24.0+，`Assetra.Api`）— PWA / 行動端前置，WPF 與 PWA 共用 `Assetra.Core` 介面
