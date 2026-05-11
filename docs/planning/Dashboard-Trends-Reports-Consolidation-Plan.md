# Dashboard / Trends / Reports 三頁重構整合計畫

**Status:** Planning
**Estimated effort:** 5–8 days（分 4 stage，可獨立交付）
**Priority:** Medium-High（影響日常使用流，但不阻擋功能開發）
**Owner:** TBD

---

## Problem

目前三個高度相關的頁面散在 navrail 的 `分析` 群組下，但分工有缺陷：

| 頁面 | 規模 | 角色 | 痛點 |
|---|---:|---|---|
| `Features/FinancialOverview/` | 389 行 XAML | KPI bar + 資產分組 accordion | **純快照**，沒有時間軸；看不到「我現在是賺是賠」的方向感 |
| `Features/Trends/` | 110 行 XAML | 單一折線 + 時間 chip | **孤立**，沒有對標、沒有區間 KPI、沒有最大回撤／波動度 |
| `Features/Reports/` | **1290 行** XAML | 三大財報 + 績效 + 風險 + 稅務 + 多年比較 | **過肥**，把「動態觀察」與「靜態財報」兩種工作流硬塞同頁 |

外加 `Features/Portfolio/Controls/AllocationView.xaml`（801 行）被埋在 Portfolio 的內 tab 裡 — 配置分析本質是儀表板問題，藏錯地方。

### 三個本質缺陷

1. **時間維度斷裂**：儀表板看「現在」、趨勢看「線」、報告看「月／年」，使用者需要在三頁之間查同一件事。
2. **績效指標放錯家**：TWR、Sharpe、最大回撤等「動態觀察」指標目前在 Reports（formal report 語意）下，應該屬於儀表板（探索性）。
3. **缺三項用戶價值最高的功能**：
   - 對標基準（vs TAIEX / 0050 / 銀行定存）
   - 區間報酬 KPI 列（絕對值 / 報酬率 / 年化 / 最大回撤）
   - 報酬日曆熱度圖（受 broker app 啟發的「每日表現」視覺）

---

## Target Design

### 3 頁 → 2 頁

#### A. 「財務儀表板」（合併 + 重構）

入口仍是 navrail `FinancialOverview`，但內部改成 **4 個平面 tab**（不巢狀）：

```
財務儀表板
├─ 總覽          ← 現 FinancialOverview，頂部加 30 天淨值 sparkline
├─ 資產趨勢      ← 現 Trends，加對標切換 + 區間 KPI 列
├─ 報酬日曆 🆕    ← 全新月曆熱度圖
└─ 配置          ← 從 Portfolio 內 tab 搬過來
```

**設計原則：**
- 4 個 tab 上限 — 超過會變認知負擔
- 不做巢狀 tab — 吃過 Portfolio 雙層 tab 的虧
- 共用 `<controls:PeriodChipBar>` 抽出來給所有 tab 復用

#### B. 「月結報告」（瘦身專注 formal report）

```
月結報告（瘦身後）
├─ 損益表
├─ 資產負債表
├─ 現金流量表
└─ 稅務（年度 / AMT / 多年比較）
```

**從 Reports 搬走的：**
- Performance（TWR / MWR / XIRR）→ 儀表板「資產趨勢」tab
- Risk metrics（Sharpe / Drawdown / Volatility / Concentration）→ 儀表板「資產趨勢」tab
- Benchmark comparison → 儀表板「資產趨勢」tab

預計 XAML 從 1290 行壓到 600 行內。

---

## Why Not Merge Everything Into One Page

| | 儀表板 | 月結報告 |
|---|---|---|
| 使用情境 | 每天打開 | 月底/季底/稅季 |
| 互動性 | 高（切時間、切對標、點月曆） | 低（讀數字、列印 PDF） |
| 心智 | 探索性 | 確認性 |
| 輸出形式 | 互動畫面 | 可列印的報表 |

兩種工作流不同，硬塞一頁兩邊都不好用。

---

## Pre-Refactor Infra Audit

| 元件 | 現況 | 重構需要做的事 |
|---|---|---|
| `IPortfolioSnapshotRepository` | ✅ 已存在，含 `PortfolioDailySnapshot(SnapshotDate, TotalCost, MarketValue, Pnl, CashValue?, EquityValue?, LiabilityValue?)` | 確認每日有寫入（不只 month-end）；補完 backfill |
| `IBenchmarkComparisonService` | ✅ Reports 內已有 | 抽到 Application 層共用（不只 Reports 用） |
| `ITimeWeightedReturnCalculator` / `IMoneyWeightedReturnCalculator` / `IXirrCalculator` | ✅ 已存在 | 直接重用 |
| Risk metrics（`IVolatilityCalculator` / `IDrawdownCalculator` / `ISharpeRatioCalculator` / `IConcentrationAnalyzer`） | ✅ 已存在 | 直接重用 |
| `PortfolioBackfillService` | ✅ 已存在 | 確認可從 trade journal 重算過去每日 net worth |
| `YahooSymbolMapper` + history fetch | ✅ 已存在 | 預先 backfill `^TWII` / `0050.TW` / `00981A.TW` 等 benchmark 歷史 |
| 月曆熱度圖控件 | ❌ 沒有 | 自製 `<controls:ReturnCalendar>`（WPF UniformGrid + DataTrigger） |
| 共用時間 chip bar | ⚠️ Trends 有但未抽 | 提到 `<controls:PeriodChipBar>` 共用 component |

---

## Stage Breakdown

> 每個 stage 可獨立 ship、獨立測試、獨立 commit。避免一次大重寫。

### Stage 1 — 「資產趨勢」加對標 + 區間 KPI（風險最低、ROI 最高）

**目標：** 不動架構，把 Reports 已有的服務接到 Trends 頁，立刻拿到對標折線 + 區間 KPI 列。

**Scope：**

1. 抽 `<controls:PeriodChipBar>` 共用元件（從現有 Trends RadioButton group）
2. `TrendsView` 主圖新增 **對標切換 toggle**：
   - 預設：只看自己（time-weighted return %）
   - 可選疊加：`^TWII`（加權指數）、`0050.TW`、`00981A.TW`、銀行定存（固定 1.5% 年化參考線）
   - 同框上限 3 條（避免麵條化）
   - 全部 normalize 到 0% 起點（同期比較）
3. 主圖下方新增 **區間 KPI 列**（5 個 card）：
   - 期初／期末淨值
   - 區間絕對損益
   - 區間報酬率（TWR）
   - 年化報酬率
   - 最大回撤
4. `PortfolioHistoryViewModel` 注入 `IBenchmarkComparisonService` + `ITimeWeightedReturnCalculator` + `IDrawdownCalculator`

**不做：**
- Reports 那邊的相同 service 暫時保留（雙寫），等 Stage 3 才搬走
- 風險 metrics（Sharpe / Volatility）這 stage 先不加

**驗收：**
- Tests 1300+ 全綠
- 切換 chip / 切換對標時，KPI 列即時更新
- 沒對標歷史時優雅 fallback（顯示「無 0050 歷史，請於設定 → 對標頁面開啟自動下載」）

**估時：** 1–1.5 天

---

### Stage 2 — Allocation 從 Portfolio 搬到財務儀表板

**目標：** 純搬，不改邏輯，把 `AllocationView` 從 Portfolio 內 tab 變成財務儀表板的「配置」tab。

**Scope：**

1. `FinancialOverviewView.xaml` 從單畫面改成 4-tab TabControl：
   - 總覽（保留現有 accordion + KPI bar）
   - 資產趨勢（嵌 Stage 1 改完的 TrendsView）
   - 報酬日曆（Stage 4 才填，先空 placeholder）
   - 配置（嵌 AllocationView，從 Portfolio 移除）
2. Portfolio 頁的內 tab 列移除「配置」項
3. `NavRailViewModel` 移除單獨的 `Nav.Trends` leaf（已併入儀表板）
4. `AppSettings.DefaultHomeSection` 保留 `FinancialOverview` 為預設

**注意：**
- `AllocationViewModel` lifecycle 要重新接 — 原本在 Portfolio 內 tab，可能依賴 PortfolioViewModel 的 position feed；移到儀表板 tab 後要確認 reload 邏輯
- 鍵盤導航 / 焦點管理：tab 切換要保留 scroll position

**驗收：**
- 4 個 tab 切換無記憶體洩漏（hot-load + dispose 正確）
- 配置 tab 進入時即時拿到最新部位（不是 stale data）
- Portfolio 頁不再看到「配置」tab

**估時：** 1–1.5 天

---

### Stage 3 — Reports 瘦身、績效／風險搬到資產趨勢

**目標：** Reports 只留 formal report，績效與風險合併進 Stage 1 的「資產趨勢」tab。

**Scope：**

1. `ReportsView.xaml` 移除 Performance / Risk 相關區塊（保留 IncomeStatement / BalanceSheet / CashFlow / TaxSummary / MultiYearTax）
2. `ReportsViewModel` 對應移除 `_performance` / `_risk` / `_attribution` / `_benchmark` / `_volatility` / `_drawdown` / `_sharpe` / `_concentration` field 與相關計算
3. 績效／風險區塊搬到「資產趨勢」tab 主圖下方（區間 KPI 列旁邊或下方）：
   - Sharpe ratio
   - Volatility (annualized)
   - Concentration (HHI + 前 N 大持股)
   - PnL Attribution（如果適合，否則保留在 Reports）
4. `ReportsServiceCollectionExtensions.cs` 對應 DI 移除
5. 評估 PnL Attribution 該留 Reports（按月） 還是搬儀表板（按區間）— TBD on review

**驗收：**
- Reports 頁 XAML ≤ 600 行
- 績效／風險指標只剩一個入口（儀表板）
- ReportsViewModel 依賴注入清單明顯減少（≥ 50% reduction）
- Tests 1300+ 全綠

**估時：** 1.5–2 天

---

### Stage 4 — 報酬日曆（全新功能）

**目標：** 月曆熱度圖，每格顯示當日淨值變動 + 顏色深淺，cell click 開明細 popover。

**Scope：**

1. **基建檢查**：確認 `PortfolioBackfillService` 能從 trade journal 重算過去每日 net worth；若沒每日 snapshot，補一次 backfill job
2. 自製 `<controls:ReturnCalendar>` 控件：
   - 6 週 × 7 天 UniformGrid
   - 每 cell 綁定 `DailyReturnVm(Date, Delta, DeltaPct, HasData)`
   - 用 `DataTrigger` 切換背景（紅／綠 + 透明度依 |Δ%| 分桶）
   - 週末（六日）用較淡的 grid line
3. 月切換 toolbar（◄ 2026/05 ►）+ 「跳到當月」按鈕
4. 月曆右側「週損益」直欄（5 週 × 損益）
5. 月曆下方迷你 bar chart（LiveCharts 可重用），顯示當月每日 PnL 直方
6. cell click：popover 顯示當日交易明細 + 帳戶餘額變動
7. zh-TW / en-US 兩個語系都要加 key

**注意：**
- daily snapshot 必須完整 — 沒資料的日子顯示 grayed out cell（不要顯示 0）
- 月末跨月的灰色 cell：要不要顯示前後月份的尾頭？建議顯示但 muted
- 紅綠語意：遵循台灣慣例（紅漲綠跌）— Assetra 已有 `AppUp` / `AppDown` 主題色

**驗收：**
- 月曆能正確顯示過去 12 個月的歷史
- 切換月份時 < 200ms 響應
- 沒 snapshot 的日期 cell 是 grayed out（非 0 紅綠）
- cell click popover 不會超出視窗

**估時：** 2–3 天（含 backfill 確認）

---

## Risks & Mitigations

| 風險 | 影響 | Mitigation |
|---|---|---|
| Stage 4 需要 daily snapshot 但歷史不全 | 月曆有大片空格 | Stage 4 開始前先跑 backfill；接受「只能看到 X 月起」的事實，加 onboarding hint |
| Reports 重構破壞既有測試 | 紅燈 | Stage 3 必須先擴測試覆蓋 Performance / Risk service 在新位置呼叫；服務本身不動只改 host VM |
| Tab 切換的記憶體洩漏 | 長期使用 RAM 漲 | 用 `Frame` / `ContentControl` + DataTemplate 而非 always-loaded TabItem，需 dispose 機制 |
| `AllocationViewModel` 從 Portfolio 搬出後失去即時 price feed | 配置 tab 顯示 stale 數字 | Stage 2 必須驗證 `IPortfolioPositionFeed` 訂閱在儀表板 host 下仍正確掛接 |
| 對標歷史下載失敗 / 配額用完 | Stage 1 對標折線缺資料 | UI 顯示「無歷史，點此重試」+ 該對標項置灰，不阻擋主圖 |

---

## Non-Goals（這次重構不做）

- ❌ 不重寫 KPI bar 內部結構（OverviewKpis 客製化機制保留）
- ❌ 不改 Reports 的稅務區塊（最近才剛完成 Stage C，動它風險高）
- ❌ 不引入新 chart library（LiveCharts2 繼續用）
- ❌ 不做「對標自訂」（這版只支援預設 4 個 benchmark：TAIEX / 0050 / 00981A / 1.5% 定存）— 之後 v2 再開
- ❌ 不做 PDF 列印（Reports 已有 export，月曆不需要）

---

## Open Questions

1. **PnL Attribution** 該留 Reports 還是搬儀表板？建議：留 Reports（屬於月報語意）；如果搬，要設計按區間（不只按月）的呼叫方式。
2. **Multi-currency users 怎麼處理對標？** 台灣使用者預設看 TWD-denominated 對標即可；多幣別使用者要不要支援 base currency 切換？建議：先用 `AppSettings.PrimaryCurrency`，不額外加 UI。
3. **報酬日曆 cell click popover 還是 detail panel？** Popover 較輕量，但若使用者想看「該日所有交易」可能需要更大畫面。建議：popover 顯示摘要 + 連結「跳到該日交易記錄」。
4. **Trends 頁的 URL deep link / scroll position 要不要保留？** 重構後 leaf 消失，使用者既有 bookmark 失效。建議：navrail click `Trends` 自動跳到儀表板的「資產趨勢」tab。

---

## File-Level Inventory

> 影響範圍清單 — 不是逐字 diff，僅標重要受影響檔。

### Created
- `Assetra.WPF/Controls/PeriodChipBar.xaml` + `.xaml.cs` — 共用時間 chip
- `Assetra.WPF/Controls/ReturnCalendar.xaml` + `.xaml.cs` — 月曆熱度圖
- `Assetra.WPF/Features/FinancialOverview/Tabs/TrendsTab.xaml`
- `Assetra.WPF/Features/FinancialOverview/Tabs/CalendarTab.xaml`
- `Assetra.WPF/Features/FinancialOverview/Tabs/AllocationTab.xaml`（thin wrapper）
- `Assetra.WPF/Features/FinancialOverview/SubViewModels/ReturnCalendarViewModel.cs`
- `Assetra.WPF/Features/FinancialOverview/SubViewModels/BenchmarkSelectionViewModel.cs`

### Modified
- `Assetra.WPF/Features/FinancialOverview/FinancialOverviewView.xaml` — 改成 4-tab 容器
- `Assetra.WPF/Features/FinancialOverview/FinancialOverviewViewModel.cs` — 新增 tab orchestration
- `Assetra.WPF/Features/Trends/TrendsView.xaml` — 重構成可內嵌的 sub-view（或直接搬入 FinancialOverview/Tabs/）
- `Assetra.WPF/Features/Portfolio/PortfolioViewModel.cs` — 移除 Allocation tab
- `Assetra.WPF/Features/Portfolio/PortfolioView.xaml` — 移除 Allocation tab
- `Assetra.WPF/Features/Reports/ReportsView.xaml` — 移除 Performance / Risk 區塊
- `Assetra.WPF/Features/Reports/ReportsViewModel.cs` — 移除對應 service 注入
- `Assetra.WPF/Shell/NavRailViewModel.cs` — 移除 `Nav.Trends` leaf
- `Assetra.WPF/Languages/zh-TW.xaml` + `en-US.xaml` — 加月曆、對標、區間 KPI 等新 key

### Deleted
- 可能：`Assetra.WPF/Features/Trends/TrendsView.xaml` + `.xaml.cs` （若內容全搬入 FinancialOverview/Tabs/）
- 評估後決定，初版傾向保留 Trends folder 作為內 tab 的實作位置

---

## Verification Checklist

每個 stage 結束時跑：

- [ ] `dotnet build Assetra.slnx` 0 error
- [ ] `dotnet test Assetra.Tests/Assetra.Tests.csproj` 1300+ pass
- [ ] 手動：4-tab 切換無視覺破圖
- [ ] 手動：對標切換時主圖即時重繪
- [ ] 手動：月曆 cell click 顯示正確的當日明細
- [ ] 手動：dark mode 顏色對比可讀（紅綠在黑底）
- [ ] Reports 頁仍可正確輸出 PDF（如果有 export）
- [ ] 中英文兩語系都正確（無 missing resource key）

---

## Out of Scope / Future Work

- v2：對標自訂（讓使用者自加 ETF / 個股當對標）
- v2：報酬日曆切換年度檢視（heatmap year overview）
- v2：報酬日曆按「絕對值」vs「百分比」切換
- v2：對標多框分屏（不同時間尺度同時看）
- v2：把 Stage 1 的區間 KPI 列做成可拖拉重排（同 KpiCards 客製化機制）

---

## Recommended Execution Order

1. ✅ **Stage 1**（1.5d）— 對標 + 區間 KPI，立刻可見的價值
2. ✅ **Stage 2**（1.5d）— 4-tab 殼 + Allocation 搬家，建立新架構
3. ✅ **Stage 3**（2d）— Reports 瘦身，績效／風險併入儀表板
4. ⏸️ **Stage 4**（3d）— 報酬日曆，最大新功能但需要 daily snapshot 基建驗證

中途的合理 stopping point：完成 Stage 1–3 後即已大幅改善（3 頁 → 2.x 頁），Stage 4 可延後到下一個 sprint。
