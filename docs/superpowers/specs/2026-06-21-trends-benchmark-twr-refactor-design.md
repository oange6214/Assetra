# 資產趨勢「vs 大盤」對比重構 — 設計文件

- 日期：2026-06-21
- 範圍：**Phase 0 + Phase 1**（Phase 0.5 KPI cliff、Phase 2 組合比較 — 本次不做，獨立立案）
- 對標參考：Google Finance「比較」UI（chips ＋ 搜尋 popup ＋ 對標表）

## 1. 問題

資產趨勢頁的「vs 大盤」% 對比圖，**我的投組那條線用「原始淨值的 %」**，被兩件事扭曲：

1. **建倉低基期** — 早期淨值低，% 正規化後早期報酬暴衝。
2. **賣股當天淨值假摔** — 賣出價金沒進當天現金快照（6/15 ~20M → 6/16 ~11.5M），% 正規化後炸成 +150% 尖峰。

結果：我的投組線衝 +150%，把 benchmark（加權指數 / 0050，皆 +6%）壓成貼地平線 → **比較項目根本看不出來**。

**根因**：benchmark 線早就用 **TWR**（時間加權報酬、除掉現金流），我的投組卻用裸淨值 % → **兩條不同基準**，UI 再漂亮也比不出東西。

次要問題：比較項目的 picker 是常駐純文字框（非 Google 那種「點開搜尋 + autocomplete 預覽」）。

## 2. 目標

1. 我的投組線改 **TWR 序列**，與 benchmark 同基準、尖峰消失、benchmark 看得出來。
2. 比較項目改 **圖上方可移除 chips**（含我的投組）。
3. **「＋比較」鈕 → 搜尋 popup ＋ autocomplete**（複用 `IStockSearchService`）。
4. 下方對標表 restyle（色點＋代號＋名稱＋同期 %）。

## 3. 架構

### Phase 0 — 我的投組線改「TWR 序列」（地基，必做）

現況：`ITimeWeightedReturnCalculator.Compute(valuations, flows)` 只回**端點** TWR（KPI 報酬率已用它，見 `PortfolioHistoryViewModel.TryComputeTwrAsync`）。需要**每日累積 TWR 序列**。

- **新增 API**：`ITimeWeightedReturnCalculator.ComputeSeries(valuations, flows)` → `IReadOnlyList<(DateOnly Date, decimal CumulativeTwr)>`，每點 = 起點到該日的累積 TWR。一次鏈式累乘 **O(n)**。
  - 端點必須等於既有 `Compute(...)`（迴歸測試錨點）。
- **`TimeWeightedReturnCalculator`**：抽出共用的「分段累乘」核心，`Compute` 取序列末值、`ComputeSeries` 回整條。
- **`PortfolioHistoryViewModel.BuildComparePercentChart`**：我的投組線從「normalize 淨值到 points[0]」改成吃 `ComputeSeries`：
  - valuations = 既有「剝建倉低值點」(median×0.05) 後的 cleaned series。
  - flows = `PerformanceFlowBuilder.BuildPerformanceFlows(trades, period)` 後 negate（投組角度，沿用 `TryComputeTwrAsync` 既有慣例）。
  - 每點 % = 累積 TWR ×100。
- 賣股當天：Sell 被當 flow 除掉 → 假摔變 ~0% → 尖峰消失。
- benchmark 線維持 `ComputeBenchmarkSeriesAsync`（單一標的無 flow，TWR ＝ 簡單價格報酬，與我的投組 TWR 同基準）。
- **日期對齊**：兩條皆「% from start」、皆日頻；以 benchmark series 的日軸為準對齊。

### Phase 1 — Google 式對比 UI（`TrendsView`）

- **1a chips**：圖上方一排 — `我的投組（不可移除）｜加權指數（不可移除）｜<custom> ✕…`，色點 ＝ 線色（沿用 `ComparisonPalette`，[0]＝我的投組）。取代現左上小圖例。
- **1b 「＋比較」鈕 + 搜尋 popup**：
  - VM：`IsComparisonPickerOpen`、`ComparisonInput`、`ComparisonSuggestions : IReadOnlyList<StockSearchResult>`。
  - `OnComparisonInputChanged` → `ComparisonSuggestions = _search?.Search(value).Take(8)`。
  - 選 suggestion → 加入 `CustomBenchmarkSymbols`（依交易所格式化：TWSE→`.TW`、TPEX→`.TWO`；指數/US 保留手打代號 fallback）→ `SaveAsync(raiseChanged:false)` → `RefreshChart()` → 關 popup。
  - XAML：`Popup`，搜尋 `TextBox` ＋ suggestions `ItemsControl`（代號＋名稱＋交易所）。
- **1c 對標表 restyle**：色點＋代號＋名稱＋同期 %；最上面加「我的投組」列（其 TWR 端點 ＝ `KpiReturnPct`）。
- **DI**：`IStockSearchService` 注入 `PortfolioHistoryViewModel`（`PortfolioDependencies` 已有 `Search`；補 ctor 參數 ＋ `PortfolioViewModel` 構建處傳 `services.Search` ＋ factory 已供給）。

### 範圍外
- **Phase 0.5**：年化（naive growth）、回撤/波動（裸 value series）仍受 cliff 影響 → 另案。
- **Phase 2**：組合當比較線需「分組每日快照」（現只有整體淨值）→ 資料層大工程、另案。

## 4. 資料流

```
LoadAsync → snapshots
  ├─ KPI：cleaned series + flows → Compute(端點) → KpiReturnPct（不變）
  └─ % 對比圖：
       我的投組：cleaned series + flows → ComputeSeries → DateTimePoint[]（新）
       benchmark：ComputeBenchmarkSeriesAsync(^TWII / customs) → DateTimePoint[]（不變）
       → BuildChart 疊多線 + chips + 對標表
比較 picker：ComparisonInput → IStockSearchService.Search → suggestions
           → 選取 → CustomBenchmarkSymbols += → Save(raiseChanged:false) → RefreshChart
```

## 5. 錯誤處理
- `_twr` / `_trades` / `_search` 任一缺 → 我的投組線 fallback 回裸淨值 %（現行行為），UI 不爆。
- 搜尋無結果 → suggestions 空、可手打代號。
- benchmark 抓取失敗 → 該線/該列顯示 "—"（沿用 `SafeBenchmarkAsync`）。
- 所有 `ObservableCollection` 變更 marshal 回 UI thread（沿用 `InvokeOnUi`）。

## 6. 測試
- `ComputeSeries`：端點 ＝ `Compute`；含 flow 的賣出日 segment ≈ 0%；建倉剝點後無爆值。
- `BuildComparePercentChart`：我的投組線取自 TWR（stub `ITimeWeightedReturnCalculator`）。
- autocomplete：`OnComparisonInputChanged` → suggestions 來自 search stub；選取 → `CustomBenchmarkSymbols` 增、Save 用 raiseChanged:false。
- 既有 stub `ITimeWeightedReturnCalculator`（測試內）補 `ComputeSeries`。

## 7. 風險
- TWR 序列與 benchmark 序列日軸對齊。
- 非 TW 標的（US/指數）符號格式 → 本次以 TW 為主、US best-effort、指數手打。
- 我（Claude）無法目視驗證 → **每階段交付你驗**：Phase 0 →「benchmark 看得出來了」→ Phase 1 →「chips/搜尋可用」。

## 8. 順序
Phase 0（TWR 線、含測試、commit）→ 你驗 → Phase 1（UI、含測試、commit）→ 你驗。
