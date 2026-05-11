# 財務概覽 Dashboard Architecture

**Status:** Living document
**Owner:** WPF UI
**Last updated:** 2026/05

短文件，給未來改動者快速理解現有設計。

---

## 核心原則

### 1. Dashboard is singular

整個 app **只有一個 dashboard 入口**：`NavSection.FinancialOverview`。
其他資產類頁面（Portfolio / CashAccounts / Liabilities / RealEstate / Insurance / Retirement / PhysicalAsset）是純工作面 — 清單 + 細節，**沒有 Dashboard tab**。

對應 anti-pattern：在資產類頁面內塞「Dashboard」內 tab。歷史上 Portfolio 曾有，現已移除（`03c4209`）。

### 2. 4 個平面 tab（不要巢狀）

```
財務概覽 (FinancialOverviewView)
├─ 總覽   ← KPI bar + widgets + accordion
├─ 資產趨勢 ← LiveCharts 主圖 + 區間 KPI + 風險指標 + 對標
├─ 報酬日曆 ← 6×8 月曆熱度圖 + bar chart + cell popover
└─ 配置   ← AllocationView（從 Portfolio 搬入）
```

TabControl 在 `Shell/NavRailView.xaml` 的 `FinancialOverviewContentTemplate` DataTemplate 內定義。每個 TabItem 的 DataContext 直接綁對應 sub-VM：

| Tab | DataContext binding |
|---|---|
| 總覽 | `Binding FinancialOverview` (FinancialOverviewViewModel) |
| 資產趨勢 | `Binding Portfolio.History` (PortfolioHistoryViewModel) |
| 報酬日曆 | `Binding Portfolio.History.ReturnCalendar` (ReturnCalendarViewModel) |
| 配置 | `Binding Portfolio.AllocationAnalysis` (AllocationViewModel) |

SelectedTab 透過 `FinancialOverviewViewModel.SelectedDashboardTab` (DashboardTab enum) 雙向綁定，支援外部 deep link（例：NavRail 攔截 `NavSection.Trends` 重導 + 設 tab）。

---

## 「總覽」tab 內部佈局

`FinancialOverviewView.xaml` 的 ScrollViewer 內 StackPanel，從上到下：

```
[Settings 齒輪] (top-right, AppIconButton)
[KPI bar]                       — 4 張 KPI 卡，使用者可設定哪 3–6 個指標
[30 天投資組合 sparkline]        — InvestmentFocusWidget.TenDaySeries 共用
[AssistantWidget]               — 1–2 條 priority insight + 「✕」可關閉
[InvestmentFocusWidget]         — 市值 / 成本 / 總損益 / 當日 PnL + sparkline
[AssetClassFocusWidget]         — 6 in 1：現金/負債/不動產/保險/退休/實物
[GoalsWidget | FireWidget]      — 2 cols side-by-side
[資產分組 accordion]             — 既有結構，保留
```

### Widget 設計模式

所有 widget 共用：
- DataContext = `FinancialOverviewViewModel`
- Widget 透過 `FinancialOverviewViewModel.{XxxWidget}` 屬性投影對應的 feature VM
- Optional 注入：對應 VM 沒注入時 widget 用 `NullToVisibilityConverter` 隱藏
- 「→ 看全部」/「→ 前往 XXX」link 用 RelayCommand + `ShellNavigationEvents.RequestNavigateTo`

**金科玉律**：widget **只投影 + 跳轉**，不允許從首頁直接編輯。要編輯一律跳到對應頁面。

### 加新資產類焦點卡

例：要新增「加密貨幣」焦點卡：

1. 加 `FinancialOverviewViewModel.CryptoFocusWidget : CryptoVM?` 屬性（optional 注入）
2. `NavigateToCryptoCommand` 用 `ShellNavigationEvents.RequestNavigateTo("Crypto")`
3. DI 在 `PortfolioServiceCollectionExtensions` 注入
4. `AssetClassFocusWidget.xaml` 的 UniformGrid 加一個 cell（或改 3 cols × 3 rows）
5. i18n key 兩個語系都加

**不要** 為新資產類做獨立 mini-dashboard / 內 tab — 這是 anti-pattern。

---

## 「資產趨勢」tab 數據鏈

`PortfolioHistoryViewModel` 從 `IPortfolioHistoryQueryService.GetSnapshotsAsync` 拉 daily snapshots，然後依使用者選的 period chip / custom range 過濾，餵到三個地方：

1. **LiveCharts 主圖** — `ValueSeries` 折線
2. **區間 KPI 列**（5 卡）— `KpiStartValue` / `KpiEndValue` / `KpiAbsolutePnl` / `KpiReturnPct` / `KpiAnnualizedPct`
   - `KpiReturnPct` 優先用 `ITimeWeightedReturnCalculator` + `ITradeRepository` 的 cash flows；都注入 → full TWR；任一缺 → fallback naive
3. **風險指標列**（4 卡）— `KpiMaxDrawdownPct` / `KpiVolatilityPct` / `KpiSharpeRatio` / `KpiHhi`
   - 各自 optional 注入 service（`IDrawdownCalculator` / `IVolatilityCalculator` / `ISharpeRatioCalculator` / `IConcentrationAnalyzer`）+ 對應 `Has*` 旗標控制 cell 可見性

對標比較區（4 個 benchmark）走 `IBenchmarkComparisonService.ComputeBenchmarkTwrAsync`；1.5% 定存基準是合成（不需 service）。

---

## 「報酬日曆」tab 結構

`ReturnCalendarViewModel` 由 `PortfolioHistoryViewModel.ReturnCalendar` 持有，在 `LoadAsync` 完成後 push 最新 snapshots。

關鍵集合：
- `Cells` (`ObservableCollection<DailyCellVm>`) — 42 個 cell，flat 視角（給 tests）
- `Weeks` (`ObservableCollection<WeekRowVm>`) — 6 列 × (7 Days + TotalDelta)，給 ItemsControl 渲染
- `AvailableMonths` (`ObservableCollection<DateOnly>`) — 月份下拉 source
- `MonthlyBarSeries` — bar chart series（紅漲綠跌兩 ColumnSeries）

Threading：`UpdateSnapshots` 從 `PortfolioHistoryViewModel.LoadAsync` 呼叫，continuation 可能在背景 thread。cstor 抓 `SynchronizationContext.Current`，`UpdateSnapshots` 偵測當前 thread 用 `Post` marshal 回 UI thread 才動 `ObservableCollection`（否則 WPF binding throw）。

### Cell click popover

`SelectCellCommand` 在 cell `HasData=true && IsCurrentMonth` 時開啟 popover；其他情況 no-op。Popover 內 ✕ 按鈕跑 `CloseCellPopoverCommand`，「查看當日交易」跑 `OpenDayTransactionsCommand` → `ShellNavigationEvents.RequestNavigateTo("TransactionLog")`。

> **TODO**：TransactionLog 目前沒有 date filter API；popover 跳過去後使用者要手動 filter。下一輪該補。

---

## navrail 結構（更新後）

```
分析 (Nav.Analysis)
├─ 財務概覽 (FinancialOverview)
├─ 月結報告 (Reports)
└─ 財務助手 (Assistant)

資產 (Nav.Assets)
├─ 投資資產 / 資金帳戶 / 負債 / 不動產 / 保險保單 / 退休專戶 / 實物資產

收支 (Nav.Cashflow)
├─ 收支分類 / 訂閱排程 / 交易記錄 / 提醒

規劃 (Nav.Planning)
├─ 目標 / FIRE / 蒙地卡羅

工具 (Nav.Tools)
└─ 稽核日誌
```

**棄用但保留的 NavSection**（為兼容舊持久化）：
- `NavSection.Trends` — `NavRailViewModel.NavigateTo` 攔截重導到 FinancialOverview + 設 Trends tab
- `PortfolioTab.Dashboard` / `.AllocationAnalysis` — TabControl SelectedValue fallback 到第一個 TabItem

---

## DI 依賴鏈

`FinancialOverviewViewModel` 注入：

```
constructor params (全 optional 含 null default)
├─ IFinancialOverviewQueryService     (required)
├─ IPortfolioPositionFeed             (required)
├─ IAppSettingsService?
├─ GoalsViewModel?                    → GoalsWidget
├─ FireViewModel?                     → FireWidget
├─ AssistantViewModel?                → AssistantWidget
├─ DashboardViewModel?                → InvestmentFocusWidget
├─ PortfolioViewModel?                → PortfolioRef (Cash/Liability cells)
├─ RealEstateViewModel?               → RealEstateFocusWidget
├─ InsurancePolicyViewModel?          → InsuranceFocusWidget
├─ RetirementViewModel?               → RetirementFocusWidget
└─ PhysicalAssetViewModel?            → PhysicalAssetFocusWidget
```

`PortfolioHistoryViewModel` 注入（也全 optional）：

```
constructor params
├─ IPortfolioHistoryQueryService      (required)
├─ ILocalizationService?
├─ IAppSettingsService?
├─ IMultiCurrencyValuationService?    (fx)
├─ IDrawdownCalculator?
├─ IBenchmarkComparisonService?
├─ ITimeWeightedReturnCalculator?
├─ ITradeRepository?                  (與 TWR 一起注入才生效)
├─ IVolatilityCalculator?
├─ ISharpeRatioCalculator?
└─ IConcentrationAnalyzer?
```

DI 註冊集中在 `Infrastructure/PortfolioServiceCollectionExtensions.AddPortfolioContext`。

---

## 測試覆蓋現況

| ViewModel | 測試檔 | 大約 |
|---|---|---|
| `PortfolioHistoryViewModel` | `PortfolioHistoryViewModelTests` | 15 個 tests（含 TWR / Risk / Benchmark 各路徑） |
| `ReturnCalendarViewModel` | `ReturnCalendarViewModelTests` | 11 個 tests（含 cells / weeks / months / popover） |

未覆蓋（補測試機會）：
- `FinancialOverviewViewModel.TopThreeGoals` 排序行為
- `AssistantViewModel.DismissInsight` 持久化路徑
- `AssetClassFocusWidget.HasAnyAssetClassFocus` 計算
- `DashboardViewModel.SparklineXAxes/YAxes`（trivial getter）

---

## v2 Features（2026/05 後續加入）

加入 5 個 v2 特性，全部由 `AppSettings` 驅動，沒有獨立 settings dialog（除非另說）。

### Calendar 色階切換（`#6a` / `f0531f6`）

`ReturnCalendarViewModel.UseAbsoluteForTone` ObservableProperty 控制 cell tone 分桶基準：
- `false`（預設）：按 `|Δ%|` 分（< 0.5% / 0.5–1.5% / 1.5–3% / ≥ 3%）
- `true`：按 `|Δ 金額|` 分（< 1K / 1K–1萬 / 1–10萬 / ≥ 10萬）

切換時不重抓資料，僅 `Rebuild()` 重算 cell tone。

### Calendar 年度熱度圖（`#6d` / `8b7f254`）

`ReturnCalendarViewModel.IsYearView` ObservableProperty：
- `false`（預設）：原本 6×8 月份 grid + weekday header + bar chart
- `true`：取代為 365/366 個 10×10 px 色塊（WrapPanel 流式），每 cell 有 tooltip

`YearViewCells` computed property 動態建構整年 cells，與 `Cells`（月份）獨立。

### KPI 重排（`#6b` / `b69440d`）

`FinancialOverviewViewModel.MoveKpiUpCommand` / `MoveKpiDownCommand`，每 KPI editor item 旁有 ▲▼ button。`ObservableCollection.Move()` 保留物件 ref；`RecomputeKpiEditorState()` 觸發 OverviewKpis 順序持久化。

### 對標自訂（`#6c` / `87eac86`）

`AppSettings.CustomBenchmarkSymbols: List<string>?` — 最多 4 個。`PortfolioHistoryViewModel.CustomBenchmarks` 集合在每次 `UpdateBenchmarksAsync` 計算 TWR。TrendsView 對標區下方 ItemsControl 展示。

無編輯 UI；使用者編輯 `settings.json` 加 `"CustomBenchmarkSymbols": ["2330.TW", "QQQ"]` 即生效。

### 資產類焦點卡客製顯示（`#6e` / `5264020`）

`AppSettings.AssetClassFocusVisibility: Dictionary<string, bool>?` — key = `Cash / Liability / RealEstate / Insurance / Retirement / Physical`。

`FinancialOverviewViewModel` 加 6 個 `IsXxxFocusVisible` computed properties：
```
public bool IsCashFocusVisible => PortfolioRef is not null && IsAssetClassVisible("Cash");
```

`AssetClassFocusWidget` 每 cell `Visibility` 綁這 6 個 property。

無編輯 UI；使用者編輯 `settings.json` 加 `"AssetClassFocusVisibility": { "Insurance": false }` 即隱藏該格。

### Skip 記錄

`#6f` 對標多框分屏永久 SKIP：螢幕容不下多 chart + 現有單框 + 4 default + 自訂對標已涵蓋用例。

---

## 改動歷史摘要

完整重構執行於 2026/05；參考 `docs/planning/Dashboard-Trends-Reports-Consolidation-Plan.md` 的 Completion Status table 取得每個 stage 對應的 commit hash。
