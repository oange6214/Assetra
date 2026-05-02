# Assetra v0.14 → v1.0 完整 Sprint 規劃

> **版號重編註記（2026-05-02，v0.22.0 修訂）**：原訂 v0.22 AI 助理 → v0.23/v0.24 多元資產 → v0.25/v0.26 情境模擬 的 4 個 sprint，最終以 **v0.22.0 一次合併出貨**（多元資產 + 情境模擬 + Wpf.Ui-first UX redesign）。AI 助理延後到後續獨立 sprint。
>
> **更早的版號重編（2026-04-28）**：v0.14–v0.19 依 roadmap 出貨；雲端同步原訂 v0.20.0 一個 sprint，實際展開為 v0.20.0–v0.20.12 + v0.21.0（GA chaos test）+ v0.21.1–v0.21.4（docs / 修補）。詳細出貨對照請見 `docs/releases/CHANGELOG.md`。

> 規劃日期：2026-04-28（v0.13.0 release 後）
> 涵蓋範圍：Roadmap 上所有未完成項目 — Phase 1 收尾、Phase 2 收尾、Phase 3、Phase 4
> 排序原則：dependency-aware（前置 schema → service → UI；外幣為多項前置）

---

## 一、Sprint 序列總覽

| 版本 | Sprint 主題 | Phase | 估算規模 | 主要產出 |
|------|------------|-------|---------|----------|
| **v0.14.0** ✅ | 外幣基礎（Currency + FX 換算） | P2.3-A | M | `Currency` VO、`FxRate`、`IFxRateProvider`、`MultiCurrencyValuationService`，既有 Performance/Risk/Reports 接入換算 |
| **v0.15.0** ✅ | 美股 / ETF Pipeline 擴充 | P2.3-B | M | US/ETF quote & history、`StockExchangeRegistry`、投資 UI 跨市場選股、Trade 流程支援多市場 |
| **v0.16.0** ✅ | Goals 完整子系統 | P1.3 | S~M | `GoalMilestone`、`GoalFundingRule`、`GoalPlanningService`、`GoalProgressQueryService` |
| **v0.17.0** ✅ | 趨勢圖增強（事件標註 + 堆疊圖） | P1.4 | S | `PortfolioEvent`、堆疊圖 schema 擴充、`TrendsView` enhancements |
| **v0.18.0** ✅ | 稅務模組 MVP | P3.1 | M | `TaxSummary`、股利所得、海外所得追蹤、報稅匯出（CSV/PDF） |
| **v0.19.0** ✅ | 進階匯入（PDF / OCR） | P3.2 | M~L | 銀行/信用卡 PDF parser、OCR adapter、PDF 匯入流程 UI |
| **v0.20.0–v0.20.12** ✅ | 雲端同步（加密 + conflict policy） | P3.4 | L（實際展開為 13 個 sub-version）| sync metadata、merge policy、加密同步層；8 entity round-trip |
| **v0.21.0** ✅ | 雲端同步 GA（chaos test）| — | S | HTTP 5xx / cancellation 路徑覆蓋；i18n 複校 no-op；staging 煙霧延後到 v1.0 GA gate |
| **v0.21.1** ✅ | 雲端同步使用者文件 + 規劃檔整理 | — | XS | `docs/guides/Cloud-Sync-Setup.md`；`docs/planning/` 6 份歷史 sprint 封存 |
| **v0.22.0** ✅ | 多元資產 + 情境模擬 + Wpf.Ui UX redesign（合併 sprint） | P4.1+P4.2 | XL | `RealEstate` / `InsurancePolicy` / `RetirementAccount` / `PhysicalAsset` 模型 + UI；`FireCalculator` + `MonteCarloSimulator`；`ui:TextBox`/`ui:Button`/`ui:SymbolIcon` 全 app 遷移；NavRail 漢堡切換；GlobalStyles 清理 -28% |
| **v0.23.0+** | AI 財務助理（自然語言查詢，從原 v0.22 延後） | P3.3 | M | LLM adapter、query intent → service routing、查詢 UI |
| **v0.24.0+** | PWA（瀏覽器端只讀視圖） | P4.3-A | L | 後端 API 抽出、PWA shell |
| **v0.25.0+** | 行動端 + 推播 | P4.3-B | L | Mobile 適配、推播通道 |
| **v1.0.0** | GA 發布（穩定性、文件、效能） | — | M | 大量整合測試、文件全面整理、效能 baseline |

> 備註：v0.22（AI）刻意排在雲端同步之後，是因為 AI 需要穩定資料來源；若不需雲端同步可提前。

---

## 二、各 Sprint 詳細計畫

### v0.14.0 — 外幣基礎

**目標**：解開「single-currency 假設」，所有計算（Performance、Risk、Reports）改走 base currency。

#### Tasks

- **D1 Currency 模型**
  - `Assetra.Core/Models/Currency.cs`：`record Currency(string Code, string Symbol, int DecimalPlaces)`，內建常數 `TWD/USD/JPY/HKD/EUR/CNY`
  - `Assetra.Core/Models/FxRate.cs`：`record FxRate(string From, string To, decimal Rate, DateOnly AsOfDate)`
  - `Assetra.Core/Interfaces/IFxRateProvider.cs`：`Task<decimal?> GetRateAsync(from, to, asOf)`、`GetHistoricalSeriesAsync`

- **F1 FxRateService（靜態 + 可選 online）**
  - `Assetra.Application/Fx/StaticFxRateProvider.cs`：使用者設定的固定匯率表（appsettings or DB table）
  - `Assetra.Application/Fx/OnlineFxRateProvider.cs`：可選 — wrap exchangerate.host / open exchange rates API
  - `Assetra.Application/Fx/CompositeFxRateProvider.cs`：先試 online，失敗 fallback 靜態
  - SQLite schema：`fx_rate(from_ccy, to_ccy, as_of_date, rate)`，唯一索引

- **F2 MultiCurrencyValuationService**
  - `Assetra.Core/Interfaces/Analysis/IMultiCurrencyValuationService.cs`
  - `ConvertAsync(amount, from, to, asOf) → decimal`
  - `ConvertSnapshotAsync(PortfolioDailySnapshot, baseCcy)`：批次換算

- **F3 既有 Service 接入換算**
  - `XirrCalculator` / `MoneyWeightedReturnCalculator` 接受 `baseCurrency` 參數，cash flow 統一換算
  - `BalanceSheetService` 將每筆 cash account / position 換算後加總
  - `ConcentrationAnalyzer.TotalCost` 換算為 base currency
  - `VolatilityCalculator` 繼續使用 base currency 序列

- **F4 UI**
  - `AccountEdit` / `PortfolioEntryEdit` 加 Currency 下拉
  - Settings 頁加「Base Currency」選項（存 `app_settings`）
  - `Languages/*.xaml`：`Settings.BaseCurrency` 等 keys

#### 測試（~15 筆）
- StaticFxRateProvider × 3、ConverterService × 4、MultiCurrencyValuation × 3、整合 BalanceSheetWithFx × 3、Concentration cross-currency × 2

#### 文件
- CHANGELOG v0.14.0
- Bounded-Contexts：新增 **Currency Context** 或併入 Analysis
- Roadmap：勾選 P2.3 前兩項

---

### v0.15.0 — 美股 / ETF Pipeline

**目標**：v0.14 已支援多幣別後，擴充標的市場。

#### Tasks

- **D1 Exchange/Market 模型**
  - `Assetra.Core/Models/StockExchange.cs`：`TWSE / TPEX / NYSE / NASDAQ / AMEX / HKEX`
  - `StockExchangeRegistry`：交易時段、tick size、settlement cycle、預設 currency
  - `PortfolioEntry.Exchange` 升級為強型別 enum

- **F1 美股 quote provider**
  - `Assetra.Infrastructure/Http/UsStockQuoteProvider.cs`：wrap Yahoo Finance / Alpha Vantage 之一
  - `IStockHistoryProvider` 多 provider routing：依 exchange 派送

- **F2 ETF 擴充**
  - 既有 `AssetType.Stock` 加 `Etf` 子分類（或於 PortfolioEntry 加 `IsEtf`）
  - ETF metadata（追蹤指數、配息頻率）

- **F3 跨市場選股 UI**
  - `StockPickerView` 加 Exchange filter
  - Trade 流程支援多市場 + 多幣別（與 v0.14 整合）

#### 測試（~10 筆）
- ExchangeRegistry × 2、QuoteProvider routing × 3、ETF metadata × 2、Trade cross-market × 3

---

### v0.16.0 — Goals 完整子系統

#### Tasks

- **D1 Goal 模型擴充**
  - `Assetra.Core/Models/GoalMilestone.cs`：`record(GoalId, TargetDate, TargetAmount, Label, IsAchieved)`
  - `Assetra.Core/Models/GoalFundingRule.cs`：定期撥款規則（金額 / 頻率 / 來源帳戶 / 目的 Goal）

- **F1 GoalPlanningService**
  - 給定目標金額 + 目標日期 + 預期報酬率，回傳建議月撥款
  - 支援「每月固定撥款」「每年加碼」「分階段」

- **F2 GoalProgressQueryService**
  - 結合 cash account balances + investment positions，計算每個 Goal 已撥款 / 進度比例
  - 接入 v0.14 多幣別

- **F3 UI 擴充**
  - `GoalsView` 加 milestone timeline、funding rule 編輯、progress bar 動畫

- **F4 與 Recurring 整合**
  - `GoalFundingRule` 可選擇實體化為 `RecurringTransaction`（重用既有 scheduler）

#### 測試（~12 筆）

---

### v0.17.0 — 趨勢圖增強

#### Tasks

- **D1 PortfolioEvent**
  - `Assetra.Core/Models/PortfolioEvent.cs`：`record(Date, Kind, Label, Description)` — 大筆買入/賣出、配息、市場事件、自訂備註
  - SQLite schema 新增 `portfolio_event` 表

- **F1 EventDetectionService**
  - 從 trade journal 自動偵測：單筆 > 10% 組合的買賣、首次配息、年度高點/低點

- **F2 堆疊圖**
  - `PortfolioDailySnapshot` schema 擴充：加 `cash_value / equity_value / liability_value` 三欄（migration）
  - `TrendsView` 切換 Line / Stacked Area

- **F3 UI**
  - `TrendsView` 折線圖加 event annotations（hover 顯示 label）
  - 切換按鈕：Line / Stacked / Both

#### 測試（~8 筆）

---

### v0.18.0 — 稅務模組 MVP

#### Tasks

- **D1 TaxSummary 模型**
  - `Assetra.Core/Models/Tax/TaxSummary.cs`：`record(Year, DividendIncomeRecords, OverseasIncomeTotal, RealizedCapitalGain, ...)`（`OverseasIncome` 為 decimal 欄位，無獨立 VO）
  - `DividendIncomeRecord`（list；無獨立 `OverseasIncomeRecord` 型別）

- **F1 TaxCalculationService**
  - 從 trade journal 抽取 `CashDividend` 聚合為股利所得（國內/海外分流，依 exchange）
  - 從 `Sell` 抽取資本利得（台股目前免稅，海外 > NT$100 萬計入）
  - 套用台灣最低稅負制（AMT）邏輯：海外所得 > 100 萬 → 計入

- **F2 TaxExportService**
  - PDF：年度稅務摘要表（QuestPDF）
  - CSV：明細，可上傳至報稅軟體

- **F3 UI**
  - `Features/Tax/TaxView`：年度切換、Summary tiles、明細 DataGrid
  - 警示：AMT 觸發、未申報資料補登提醒

#### 測試（~10 筆）

---

### v0.19.0 — 進階匯入（PDF / OCR）

#### Tasks

- **D1 PDF parser 抽象**
  - `IPdfStatementParser`：抽取 `ImportPreviewRow[]`
  - 子類：`BankStatementPdfParser` / `CreditCardStatementPdfParser`

- **F1 PdfPig 整合**（NuGet `PdfPig` 純 .NET，不依賴外部）
  - 文字模式 PDF：直接 extract text
  - 圖片模式 PDF：轉送 OCR

- **F2 OCR adapter**
  - `IOcrAdapter`：抽象介面
  - 預設實作：Tesseract.NET（離線）；可選 Azure Vision / Google Vision

- **F3 既有匯入流程整合**
  - `ImportFormatDetector` 加 PDF detection
  - Preview UI 顯示 OCR 信心分數，低於 threshold 標紅供使用者確認

#### 測試（~8 筆）+ 真實 PDF samples

---

### v0.20.0 — 雲端同步

> ⚠ 高風險 sprint：加密、merge conflict、隱私。先做 read-only 同步，再做雙向。

#### Tasks

- **D1 Sync metadata**
  - `SyncMetadata`：last sync timestamp、device id、change log 起點
  - `EntityVersion`：每個 mutable entity 加 `version + last_modified_at + last_modified_by_device`

- **F1 加密層**
  - 客戶端 AES-256 加密（用使用者密語派生金鑰）
  - 雲端只存 ciphertext blob

- **F2 Merge policy**
  - Last-write-wins（簡單初版）
  - Conflict UI：兩端時間戳 + 內容差異 → 使用者選擇

- **F3 Backend 選擇**
  - 候選：自架 Supabase / Cloudflare R2 + workers
  - 先做抽象介面 `ICloudSyncProvider`，預設 Supabase

- **F4 UI**
  - `Settings/Sync`：登入、密語、同步狀態、衝突列表

#### 測試（~15 筆）+ 整合測試（雙裝置模擬）

---

### v0.23.0+ — AI 財務助理（自然語言查詢）  *(從原 v0.22 延後)*

> 此 sprint 因 v0.22.0 合併出貨多元資產 + 情境模擬 + Wpf.Ui UX
> redesign，AI 助理被排到下一個獨立 sprint。任務內容沿用原規劃。

#### Tasks

- **D1 LLM adapter**
  - `ILlmProvider`：`AskAsync(prompt, context) → string`
  - 預設實作：Claude / OpenAI（API key 在 user-secrets）

- **F1 Query intent router**
  - 將使用者問題分類為：lookup（單一資料）/ aggregate（聚合）/ analysis（分析）
  - 動態組裝 context（最近 trade、portfolio summary）→ tool calling

- **F2 Tool catalog**
  - 暴露 `GetPortfolioValue`、`ListRecentTrades`、`GetIncomeStatement` 等只讀 tool
  - LLM 透過 tool calling 取資料 → 回答自然語言

- **F3 UI**
  - `Features/Assistant/AssistantView`：聊天介面、conversation history、引用資料連結

#### 測試（~8 筆）

---

### ✅ v0.23.0 — 多元資產（不動產 + 保險）  *(已於 v0.22.0 出貨)*

**目標**：讓 BalanceSheet 完整反映不動產與保險資產，`ConcentrationAnalyzer` 納入新資產類別。

#### Tasks

- **D1 RealEstate 模型**
  - `Assetra.Core/Models/MultiAsset/RealEstate.cs`：`record(Id, Name, Address, Category, PurchasePrice, CurrentValue, ValuationMethod, MortgageLoanId?, Currency, EntityVersion)`
  - `Assetra.Core/Models/MultiAsset/RentalIncomeRecord.cs`：`record(RealEstateId, YearMonth, Amount, AccountId)`
  - SQLite schema：`real_estate`、`rental_income_record` 表

- **D2 InsurancePolicy 模型**
  - `Assetra.Core/Models/MultiAsset/InsurancePolicy.cs`：`record(Id, Name, Category, AnnualPremium, CoverageAmount, StartDate, EndDate, CashValue, EntityVersion)`
  - `Assetra.Core/Models/MultiAsset/InsurancePremiumRecord.cs`：`record(PolicyId, Year, Amount, AccountId)`
  - SQLite schema：`insurance_policy`、`insurance_premium_record` 表

- **F1 Repository 與 Service**
  - `IRealEstateRepository` / `RealEstateSqliteRepository`
  - `IInsurancePolicyRepository` / `InsurancePolicySqliteRepository`
  - `RealEstateValuationService`（目前估值、貸款餘額淨值）
  - `InsuranceCashValueCalculator`

- **F2 Reporting 接入**
  - `BalanceSheetService`：資產端新增「不動產淨值」、「保單現金價值」行項
  - `IncomeStatementService`：收入端新增「租金收入」；費用端新增「保費支出」

- **F3 Analysis 接入**
  - `ConcentrationAnalyzer`：新增 `RealEstateWeight` / `InsuranceWeight` 佔淨值比例
  - 集中度警示門檻可設定

- **F4 UI**
  - `Features/MultiAsset/RealEstateView`：CRUD + 貸款關聯 + 租金收入列表
  - `Features/MultiAsset/InsuranceView`：CRUD + 保費記錄 + 現金價值曲線
  - 儀表板新增「多元資產」摘要卡片
  - `Languages/*.xaml`：新增對應 key

- **F5 Sync 整合**
  - `RealEstate` / `InsurancePolicy` 加入 `CompositeLocalChangeQueue` entity routing

#### 測試（~14 筆）
- RealEstate CRUD × 3、InsurancePolicy CRUD × 3、BalanceSheet 接入 × 3、ConcentrationAnalyzer 新類別 × 3、Sync routing × 2

#### 文件
- CHANGELOG v0.23.0
- Bounded-Contexts §13a / §13b 確認
- Roadmap 勾選

---

### ✅ v0.24.0 — 多元資產（退休 + 實物資產）  *(已於 v0.22.0 出貨)*

**目標**：補齊退休專戶與實物資產，BalanceSheet 完整涵蓋所有資產類別。

#### Tasks

- **D1 RetirementAccount 模型**
  - `Assetra.Core/Models/MultiAsset/RetirementAccount.cs`：`record(Id, Name, Category, Balance, EmployeeContribution, EmployerContribution, YearsOfService, LegalWithdrawalAge, Currency, EntityVersion)`
  - `Assetra.Core/Models/MultiAsset/RetirementContribution.cs`：`record(AccountId, Year, EmployeeAmount, EmployerAmount)`
  - SQLite schema：`retirement_account`、`retirement_contribution` 表

- **D2 PhysicalAsset 模型**
  - `Assetra.Core/Models/MultiAsset/PhysicalAsset.cs`：`record(Id, Name, Category, AcquisitionCost, CurrentValue, ValuationMethod, AcquiredDate, Currency, EntityVersion)`
  - SQLite schema：`physical_asset` 表

- **F1 Repository 與 Service**
  - `IRetirementAccountRepository` / `RetirementAccountSqliteRepository`
  - `IPhysicalAssetRepository` / `PhysicalAssetSqliteRepository`
  - `RetirementProjectionService`（預估退休時餘額，複利計算）
  - `PhysicalAssetValuationService`

- **F2 Reporting 接入**
  - `BalanceSheetService`：資產端新增「退休專戶」、「實物資產」行項
  - 淨資產計算完整涵蓋所有 6 類資產

- **F3 Goals 接入**
  - `GoalProgressQueryService`：可指定「退休專戶餘額」作為 Goal 進度來源

- **F4 Analysis 接入**
  - `ConcentrationAnalyzer`：新增 `RetirementWeight` / `PhysicalAssetWeight`

- **F5 UI**
  - `Features/MultiAsset/RetirementView`：CRUD + 提撥記錄 + 預估餘額圖
  - `Features/MultiAsset/PhysicalAssetView`：CRUD + 估值歷史
  - 儀表板多元資產卡片補齊退休 + 實物

- **F6 Sync 整合**
  - `RetirementAccount` / `PhysicalAsset` 加入 entity routing

#### 測試（~12 筆）
- RetirementAccount CRUD × 3、PhysicalAsset CRUD × 2、RetirementProjection × 2、BalanceSheet 完整 6 類 × 3、Goals 接入 × 2

#### 文件
- CHANGELOG v0.24.0
- Bounded-Contexts §13c / §13d 確認

---

### ✅ v0.25.0 — 情境模擬（FIRE 計算機）  *(已於 v0.22.0 出貨)*

**目標**：讓使用者輸入財務假設，立即得到「幾歲能 FIRE」與「退休後能撐幾年」的試算結果。

#### Tasks

- **D1 模型（純計算，無持久化）**
  - `Assetra.Core/Models/Simulation/FireScenario.cs`：`record(CurrentNetWorth, MonthlySavings, AnnualReturnRate, AnnualExpenseInRetirement, InflationRate, CurrentAge)`
  - `Assetra.Core/Models/Simulation/FireResult.cs`：`record(YearsToFire, FireAge, ProjectedWealthAtFire, SustainabilityPercent)`
  - `Assetra.Core/Models/Simulation/WithdrawalProjection.cs`：`record(Year, StartBalance, Withdrawal, Return, EndBalance, Depleted)`

- **F1 FireCalculator**
  - `Assetra.Application/Simulation/FireCalculator.cs`
  - 確定性試算：每年累積儲蓄 + 複利，直到淨值 ≥ 年支出 / SWR
  - 支援 4% 法則（SWR = 0.04）或自訂提領率
  - 輸出 `FireResult`

- **F2 SustainabilityAnalyzer**
  - `Assetra.Application/Simulation/SustainabilityAnalyzer.cs`
  - 給定退休資產 + 年支出 + 通膨 + 報酬率，逐年模擬 30 年提領
  - 輸出：`WithdrawalProjection[]`（每年餘額）+ 是否在 30 年內耗盡

- **F3 UI**
  - `Features/Simulation/FireView`
  - 輸入表單：目前淨資產（從 Portfolio 自動帶入）、月儲蓄、預期報酬率、目標年支出、通膨率
  - 輸出：FIRE 達成年數 / 年齡、達成時資產、30 年提領曲線圖（LineChart）
  - 警示：若 30 年內資產耗盡，顯示「不可持續」提示

- **F4 Goals 整合**
  - `FireResult.FireAge` 可直接作為退休 Goal 的預測達成日

#### 測試（~10 筆）
- FireCalculator 基本試算 × 3、SWR 邊界 × 2、SustainabilityAnalyzer × 3、Goals 整合 × 2

#### 文件
- CHANGELOG v0.25.0
- Bounded-Contexts §14a 確認

---

### ✅ v0.26.0 — 情境模擬（Monte Carlo）  *(已於 v0.22.0 出貨)*

**目標**：加入隨機變數，輸出財務走勢的機率分布，讓試算從「單一線」變成「扇形區間」。

#### Tasks

- **D1 模型（純計算，無持久化）**
  - `Assetra.Core/Models/Simulation/MonteCarloScenario.cs`：`record(FireScenario Base, int Simulations, RateDistribution ReturnDist, RateDistribution InflationDist)`
  - `Assetra.Core/Models/Simulation/RateDistribution.cs`：`record(double Mean, double StdDev)`（常態分布參數）
  - `Assetra.Core/Models/Simulation/MonteCarloResult.cs`：`record(double SuccessRate, decimal[] P10, decimal[] P50, decimal[] P90, int MedianDepletionYear?)`

- **F1 MonteCarloSimulator**
  - `Assetra.Application/Simulation/MonteCarloSimulator.cs`
  - 執行 N 次（預設 1000）獨立試算，每次從常態分布取樣報酬率 + 通膨率
  - 統計各年 10/50/90 百分位餘額
  - 成功率 = 30 年後仍有餘額的比例
  - 使用 `Random.Shared` + Box-Muller 轉換（不依賴外部套件）

- **F2 StochasticRateProvider**
  - `Assetra.Application/Simulation/StochasticRateProvider.cs`
  - 提供歷史報酬率分布預設值（台灣大盤、S&P 500、保守債券）

- **F3 UI**
  - `Features/Simulation/MonteCarloView`
  - 繼承 FireView 的輸入，加上「報酬率標準差」、「模擬次數」設定
  - Fan chart：三條帶（P10 / P50 / P90）疊加在確定性試算線上
  - 成功率大字顯示：「在 1000 次模擬中，XX% 的情境下 30 年後仍有餘額」
  - 使用 LiveChartsCore（已有）實作 fan chart

#### 測試（~8 筆）
- MonteCarloSimulator 統計特性 × 3、RateDistribution 邊界 × 2、SuccessRate 計算 × 2、UI 資料綁定 × 1

#### 文件
- CHANGELOG v0.26.0
- Bounded-Contexts §14b 確認

---

### v0.24.0+ / v0.25.0+ — 多端體驗  *(原 v0.27 / v0.28，因 v0.22.0 合併出貨往前移 3 號)*

> 重大架構決策：原 WPF Repository 直連 SQLite → 必須先抽出 Web API。

#### v0.24.0+：PWA（read-only）

- ASP.NET Core Web API 專案 `Assetra.Api`
- WPF + PWA 共用 `Assetra.Core` 介面
- PWA：dashboard、portfolio overview、reports（read-only）

#### v0.25.0+：行動端 + 推播

- 候選技術：MAUI / React Native / Flutter
- 推播通道：定期任務、預算超支、異常交易

---

### v1.0.0 — GA 發布

#### Tasks

- 整體效能 baseline + profiling
- E2E 測試覆蓋（critical user flows）
- 文件全面整理（README、Architecture、User Guide）
- Installer / auto-update（Squirrel.Windows / WiX）
- 隱私聲明 + 授權

---

## 三、Cross-Cutting 工程任務

### 架構守則
- 所有新功能優先進 `Application` layer，禁止 WPF 直接碰 repository
- 跨 context 共用邏輯抽到 `Assetra.Core/Interfaces/`
- v0.20 後 sync metadata 為強制需求 → 所有新 entity 自帶 version

### 測試策略
- 每 sprint 新增 service 對應 unit tests
- v0.19+ 整合測試比重提高（PDF parsing、OCR、sync）
- v1.0 前補 E2E（headless WPF 或 PWA Playwright）

### 文件節奏
- 每 sprint 結束更新 CHANGELOG + Bounded-Contexts + Roadmap
- v0.20 / v1.0 各做一次 architecture overview 重寫

---

## 四、執行建議

1. **逐個 sprint 順序執行**：v0.14 → v0.25.0+ → v1.0（中途 v0.22.0 一次合併出貨原 v0.23–v0.26）
2. **每完成一個 sprint**：build / test / docs / commit / tag / push
3. **遇到大規模 schema 改動**（v0.14 多幣別、v0.20 sync、v0.24.0+ API）：先做 spike POC，不行再退回
4. **若使用者中途決定停在 MVP**：合理的 stop point 在 **v0.18.0**（核心理財 + 投資 + 稅務完整），v0.19+ 屬「進階自動化 / 多平台」

---

## 五、本次執行範圍

依使用者指示「**剩下的一次依序做完**」，本 plan 將從 **v0.14.0 開始依序執行至 v1.0.0**。每個 sprint 完成後自動：
1. 建置全綠（`dotnet build`）
2. 全測試綠（`dotnet test`）
3. 更新文件（CHANGELOG / Bounded-Contexts / Roadmap）
4. Commit + tag + push（master）
5. 進入下一個 sprint

**預估時程**：v0.14 ~ v1.0 共 16 個 sprint（v0.14–v0.21.1 已出貨 8 個；v0.22–v0.28 + v1.0.0 待做 8 個；v0.20.x 內部展開為 13 個 sub-version 不計入），每 sprint 約 2~5 小時實作（不含外部依賴整合測試）。
