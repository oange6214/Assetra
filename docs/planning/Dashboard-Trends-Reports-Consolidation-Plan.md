# Dashboard / Trends / Reports 三頁重構整合計畫

**Status:** ✅ **COMPLETED** (with notes — see Completion section)
**Estimated effort:** 6–9 days（分 5 stage，可獨立交付）
**Actual effort:** 多輪 session 累積 ≈ 6–8 天等效工時
**Priority:** Medium-High（影響日常使用流，但不阻擋功能開發）
**Owner:** TBD

---

## Completion Status (2026/05)

| Stage | 狀態 | 主要 commits |
|---|---|---|
| Stage 1 — 對標 + 區間 KPI | ✅ Done | `6086c3d`（對標 + KPI 列）/ `d586190`（升級 full TWR） |
| Stage 2 — 4-tab shell + Allocation 搬家 | ✅ Done | `0c6bdbc` |
| Stage 2.5 — Goals / FIRE / Assistant widgets | ✅ Done | `229724a`（widget）/ `65afcfa`（onboarding polish + dismiss persist） |
| Stage 3 — Reports 瘦身、風險指標搬到 Trends | ✅ Done | `5c5cd44`（hide Performance Expander）/ `26116aa`（Volatility/Sharpe/HHI 整合）/ `54f9bb0`（跳過 hidden 計算） |
| Stage 4 — 報酬日曆 heatmap | ✅ Done | `845f6fe`（基礎）/ `e457ab5`（thread fix）/ `1c25d99`（紅綠 KPI + bar chart）/ `12d2365`（週欄 + 月份下拉 + popover）/ `d0b6a2f`（DateOnly bind crash 修復） |

**附加工作（plan 外但同期完成）：**

| 工作 | Commit |
|---|---|
| 命名「財務儀表板 → 財務概覽」 | `e5052c4` |
| navrail 群組重組（總覽 → 分析 + 工具） | `e5052c4` |
| 移除 Portfolio.Dashboard 內 tab（dashboard singleton） | `03c4209` |
| 投資資產頁去掉單一 tab 包裝 | `d2b2f9d` |
| 資產類焦點卡（現金/負債/不動產/保險/退休/實物） | `0631600`, `ba30f3d` |
| 30 天 sparkline（投資焦點 + KPI bar 下方） | `342ee63` |
| Reports performance/risk hidden 但仍計算 → 改跳過 | `54f9bb0` |
| Title bar「新增」按鈕 icon-only 化 | `1bed7eb` |
| Cash/Liability 按鈕 label 一致化 | `1bed7eb` |
| 收支分類 Fluent icons | `24bafff`, `42e95bb`, `a5ea13a` |

**Tests：** 1300 → **1321**（+21 新測試，全綠）

---

## 已知遺留事項（plan 範圍外或下一輪處理）

| 項目 | 狀態 |
|---|---|
| ~~`ReportsViewModel._risk` / `_performance` fields + 8 個 service 注入~~ | ✅ Done in `728b479`（-459 行） |
| ~~Daily net worth snapshot 持久化 CashValue/LiabilityValue~~ | ✅ Done in `29ff3c5` |
| ~~`PerformanceFlowBuilder` 只認 Buy/Sell/CashDividend~~ | ✅ Done in `a2aa71b`（加 PerformanceFlowScope enum） |
| ~~`TransactionLog` 日期 filter API~~ | ✅ Done in `a2aa71b`（Calendar popover 帶日期跳轉） |
| ~~`DismissedAssistantInsights` 雲端同步 schema~~ | ✅ Done in `4302d21`（JSON 序列化 + tests） |

## v2 Features Completion (commits 2026/05)

| 任務 | 狀態 | Commit |
|---|---|---|
| #6a Calendar 絕對值/百分比色階切換 | ✅ Done | `f0531f6` |
| #6b KPI 列重排（▲▼ 按鈕） | ✅ Done | `b69440d` |
| #6c 對標自訂（settings.json 驅動 + 顯示） | ✅ Done | `87eac86` |
| #6d Year heatmap（GitHub-style 切換） | ✅ Done | `8b7f254` |
| #6e 資產類焦點卡客製顯示 | ✅ Done | `5264020` |
| #6f 對標多框分屏 | ⏭️ SKIP | 永久 — 螢幕容不下 + 現有方案夠用 |

v2 tests：`45cc326`（+5 test cases）

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

> 命名考量見最後「Naming」附錄；plan body 仍沿用「財務儀表板」以保留現有 i18n key / 類名。

入口仍是 navrail `FinancialOverview`，但內部改成 **4 個平面 tab**（不巢狀），且「總覽」tab 內嵌 3 個 widget 把 navrail 其他 leaf 的高 glance 資訊 pull-up 到首頁：

```
財務儀表板
├─ 總覽 ──────────────────────────────────────────
│   ├─ KPI bar（淨值/總資產/投資/負債）+ 30 天淨值 sparkline
│   ├─ 🆕 助手 insights widget（1–2 條 pill，可關閉 / 跳轉 Assistant 頁）
│   ├─ 🆕 目標進度 widget（前 3 個 active goal）
│   ├─ 🆕 FIRE 進度 widget（單卡：FI 進度 + 倒數年）
│   └─ 資產分組 accordion（保留現有）
│
├─ 資產趨勢      ← 現 Trends，加對標切換 + 區間 KPI 列
├─ 報酬日曆 🆕    ← 全新月曆熱度圖
└─ 配置          ← 從 Portfolio 內 tab 搬過來
```

**設計原則：**
- 4 個 tab 上限 — 超過會變認知負擔
- 不做巢狀 tab — 吃過 Portfolio 雙層 tab 的虧
- 共用 `<controls:PeriodChipBar>` 抽出來給所有 tab 復用
- **widget ≠ 整頁搬家**：Goals / FIRE / Assistant 仍保留 navrail 獨立 leaf；widget 是首頁的 summary + 跳轉入口
- 每個 widget 都可一鍵跳到對應的詳細頁（保留現有工作流）

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

### Stage 2.5 — 「總覽」tab 加 Goals / FIRE / Assistant widget

**目標：** 把高 glance value 的 leaf 內容以 widget 形式 pull-up 到首頁，獨立頁仍保留。

**Scope：**

1. **Goals widget**（前 3 個 active goal）
   - 每個 row：goal name + 進度條 + 達成 % + 預計達成日（或剩餘天數）
   - 點 widget header 跳轉 `NavSection.Goals`
   - 點 single row 跳轉並 highlight 該 goal
   - 來源：`IGoalRepository` 或 `GoalsViewModel` 投影；不重複 service
2. **FIRE widget**（單張卡）
   - 大數字：FI 進度 %（current net worth / FI target）
   - 副資訊：FI 倒數年（按目前儲蓄率 / 報酬假設推算）
   - 點卡片跳轉 `NavSection.Fire`
   - 來源：`FireViewModel` 既有計算結果投影
3. **Assistant insights widget**（1–2 條 pill）
   - 只顯示優先序最高的 1–2 條 insight（既有 `IAssistantInsightService` 應已有排序）
   - 每條 pill 有「✕ 關閉本條」+ 「全部 →」連到 Assistant 頁
   - 已關閉的 insight 用 `AppSettings.DismissedInsightIds`（如沒則新增）持久化
4. **跨 widget 共用：** `<controls:DashboardWidgetCard>` — 標題列 + 內容區 + footer link 的統一外框

**驗收：**

- 空狀態：沒設 goal / 沒 FIRE 目標 / 沒 insight 時，widget 顯示 onboarding hint + 跳轉鈕，不是空白
- 點 widget 任何位置都能正確跳到對應頁面
- 三個 widget 加總高度不應超過 KPI bar + accordion 一倍（避免「總覽」變太長）
- Goals / Fire / Assistant 三個獨立頁 **完全不動**（lifecycle / state 不重複）
- Tests 1300+ 全綠（不破壞既有 Goals / Fire / Assistant tests）

**注意：**

- widget 應「**只讀投影**」，不允許從首頁直接編輯 goal / FIRE 目標；編輯必須跳到獨立頁完成（避免兩處編輯邏輯）
- Assistant insight 的「關閉」要與 Assistant 頁同步（不能首頁關了詳細頁還顯示）

**估時：** 1 天

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
5. **Stage 2.5 — Assistant insight 的關閉持久化** 該寫進 `AppSettings.DismissedInsightIds`（user-scope）還是 per-insight `IsDismissed` flag（DB-scope）？建議：DB-scope，因為 insight 跟資料連動（用 insight id + dismiss timestamp），且雲端同步較自然。
6. **Stage 2.5 — Goals widget 是否分「短期 / 長期」？** 若使用者有多個 active goal，前 3 個怎麼挑？建議按「最近到期日」優先，並提供「→ 看全部」連結。
7. **命名是否同時改？** 見 Appendix A。建議：跟主重構分開，當小 PR 處理，方便回滾。

---

## File-Level Inventory

> 影響範圍清單 — 不是逐字 diff，僅標重要受影響檔。

### Created
- `Assetra.WPF/Controls/PeriodChipBar.xaml` + `.xaml.cs` — 共用時間 chip
- `Assetra.WPF/Controls/ReturnCalendar.xaml` + `.xaml.cs` — 月曆熱度圖
- `Assetra.WPF/Controls/DashboardWidgetCard.xaml` + `.xaml.cs` — 統一的 widget 外框
- `Assetra.WPF/Features/FinancialOverview/Tabs/TrendsTab.xaml`
- `Assetra.WPF/Features/FinancialOverview/Tabs/CalendarTab.xaml`
- `Assetra.WPF/Features/FinancialOverview/Tabs/AllocationTab.xaml`（thin wrapper）
- `Assetra.WPF/Features/FinancialOverview/SubViewModels/ReturnCalendarViewModel.cs`
- `Assetra.WPF/Features/FinancialOverview/SubViewModels/BenchmarkSelectionViewModel.cs`
- `Assetra.WPF/Features/FinancialOverview/Widgets/GoalsWidget.xaml` + `.xaml.cs` + `GoalsWidgetViewModel.cs`（Stage 2.5）
- `Assetra.WPF/Features/FinancialOverview/Widgets/FireWidget.xaml` + `.xaml.cs` + `FireWidgetViewModel.cs`（Stage 2.5）
- `Assetra.WPF/Features/FinancialOverview/Widgets/AssistantWidget.xaml` + `.xaml.cs` + `AssistantWidgetViewModel.cs`（Stage 2.5）

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
3. ✅ **Stage 2.5**（1d）— Goals / FIRE / Assistant widget 進「總覽」
4. ✅ **Stage 3**（2d）— Reports 瘦身，績效／風險併入儀表板
5. ⏸️ **Stage 4**（3d）— 報酬日曆，最大新功能但需要 daily snapshot 基建驗證

中途的合理 stopping point：完成 Stage 1–3 後即已大幅改善（3 頁 → 2.x 頁 + 首頁更有資訊密度），Stage 4 可延後到下一個 sprint。

---

## Appendix A — Naming（命名建議）

`財務儀表板` 三個字有兩個小問題：

1. **「儀表板」是技術 / SaaS 用語**，台灣使用者對它的直覺較弱
2. 重構後它**不只是儀表板** — 有 4 個 tab 含日曆 / 趨勢 / 配置，「儀表板」嚴格上指 KPI 卡盤

### 候選名稱比較

| 名稱 | 優點 | 缺點 |
|---|---|---|
| **財務儀表板**（保留） | 已熟悉、commit history / tests / Settings 都引用 | 略技術；不完全準確 |
| **財務概覽** ⭐ | 涵蓋 4 tab、不偏 KPI、用詞自然、對齊 Apple/Intuit 的「Overview」語言 | 跟 navrail 群組「總覽」字面有點近 |
| 財務首頁 | 暗示這是預設落地頁 | 偏實作語意 |
| 我的財務 | 親近、口語 | 不夠正式 |
| 財務全景 | 精確（panorama） | 略文青 |
| 淨值與走勢 | 描述準確 | 太長、太硬 |
| 財富中心 | 行銷感強 | 不符 Assetra 工具型定位 |

### 推薦：二擇一

| 選 | 何時選 | 改名成本 |
|---|---|---|
| 改成「**財務概覽**」 | 重構是順手改名好時機，擺脫「儀表板≈純 KPI」誤導 | 只改 `FinancialOverview.Nav.Label` 等 i18n value（保留 key 名），**約 4–6 個 resource string** |
| 保留「**財務儀表板**」 | 不確定改名價值；既有 doc / commit 引用穩定 | 0 |

### 命名重構不動的東西（無論選哪個）

- `NavSection.FinancialOverview` enum
- `Assetra.WPF/Features/FinancialOverview/` 資料夾
- `FinancialOverviewViewModel` 類名
- i18n key（`FinancialOverview.Nav.Label`、`FinancialOverview.Kpi.*` 等）

避免「英文識別子大規模 rename」帶來的 PR 噪音與 merge conflict 風險。

---

## Appendix B — navrail 群組重組（可選，獨立 PR）

群組目前叫「**總覽 (Nav.Overview)**」，但裡面其實混了兩種語意：

| 現群組成員 | 語意 |
|---|---|
| 財務儀表板 / 資產趨勢 / 月結報告 | **分析 / 觀察** |
| 財務助手 / 稽核日誌 | **工具 / 診斷** |

### 重組建議

```
原：總覽（5 leaf 混語意）
   ├─ 財務儀表板 / 資產趨勢 / 月結報告 / 財務助手 / 稽核日誌

後：分析（語意收斂）
   ├─ 財務儀表板（含 4 tab）
   ├─ 月結報告
   └─ 財務助手        ← 留分析群組，因為 AI insight 是分析輸出

  工具（新群組或併入既有底部）
   └─ 稽核日誌        ← 屬於診斷而非日常分析
```

### 為何「資產趨勢」leaf 消失

Stage 2 已併入儀表板 tab；舊 leaf 點擊行為改為：跳到儀表板 + 預設 active tab = 「資產趨勢」（deep link 行為），舊 keyboard bookmark / muscle memory 不破壞。

### 風險

- AuditLog 搬到新群組會打破現有導航流；如果使用者習慣從「總覽」找它，要評估
- 「分析」這個群組名跟「規劃 (Planning)」可能語意重疊；可改用「**觀察**」或「**動態**」
- 建議當獨立 PR，不要跟主重構綁定，降低 review 負擔

---

## Appendix C — Widget 與獨立頁的職責切分（給 Stage 2.5 用）

| 元件 | 角色 | 編輯權限 | 跳轉行為 |
|---|---|---|---|
| 儀表板 Goals widget | 投影前 3 個 goal | **只讀** | 點 row → Goals 頁 + highlight 該 goal |
| Goals 獨立頁 | CRUD + 詳細管理 | 可編輯 | — |
| 儀表板 FIRE widget | 投影 FI 進度數字 | **只讀** | 點卡 → Fire 頁 |
| Fire 獨立頁 | 設目標、調報酬假設、看模擬 | 可編輯 | — |
| 儀表板 Assistant widget | 1–2 條 priority insight | 可「✕ 關閉本條」 | 點 pill → Assistant 頁 |
| Assistant 獨立頁 | 全列表 + 已關閉項管理 | 可編輯 | — |

**金科玉律：widget 不重複實作獨立頁的編輯邏輯。** 所有「改變狀態」的動作（編輯 goal、調整 FIRE 假設、永久 dismiss insight）都跳到獨立頁完成。Widget 只投影 + 跳轉。
