# Assetra vs AscentPortfolio Asset Detail Panel 對比

對比來源：2026-05-21 user 提供 AscentPortfolio 兩張截圖 + Assetra 現況截圖。
場景：點 portfolio row 開啟的「資產 detail side panel」(滑出右側面板)。

## TL;DR

AscentPortfolio 在 **資訊深度** + **視覺一致性** 上贏，
但 Assetra 在 **背後功能完整度**（多幣別、tx 結構、status bar、NavRail IA）贏。

值得借的 8 項中：**前 3 項是真價值**（KPI 表 / 已實現拆解 / inline chart），
後 5 項是視覺 polish。**從前 3 項開始**。

不要為了模仿改 brand color（紫色 vs Navy/Gold）— 那是品牌決定不是改進方向。

---

## 對比矩陣

### 資訊深度（功能性差距）

| 元素 | AscentPortfolio | Assetra 現況 | gap |
|------|-----------------|--------------|-----|
| KPI 矩陣 | 投報率(ROI) + 年化(XIRR) × 1年/3年/累積 = 6 個數字 | ❌ 完全沒 | 🔴 大 |
| 已實現損益拆解 | 總已實現 + 資本利得 + 股息收入 | ❌ 沒分項 | 🔴 大 |
| Inline 價格圖 | 5D / 1MO / 6MO / 1Y / 5Y / MAX timeline + 「價格/我的價值」 toggle | ❌ asset 級沒 chart | 🟡 中 |
| 資產屬性 tags | EQUITY (灰底) + USD (紫底) 並排 chip badges | 🟡 只有單個「股」 badge | 🟢 小 |

### 視覺（風格差距）

| 元素 | AscentPortfolio | Assetra 現況 | gap |
|------|-----------------|--------------|-----|
| 主 metrics layout | 2×2 = 4 張大卡（聚焦） | 2×4 = 7 張小卡（密度高失焦） | 🟡 中 |
| Selected card 強調 | 點選的 card 紫底邊框 = 視覺主角 | 沒 selected state | 🟢 小 |
| 主 CTA | 單顆「+ 新增紀錄」紫色 primary | 「+ 買入 / 賣出」並排兩顆 | 🟢 小 |
| Tab style | pill bg (concept + bg colored) | underline tab | 🟢 小 |

### Assetra 已勝出的（不要退化）

| 元素 | Assetra | 別退化 |
|------|---------|--------|
| 後端 trade / 多幣別 / FX / cash account 整合度 | ✅ 完整 | 動 panel 不要砍 |
| NavRail 4 大類資訊架構 | ✅ Wealth Management 級 | 不重排成 flat list |
| Status bar 三重資訊（市場/同步/P&L） | ✅ 比 AscentPortfolio 強 | 維持 |
| Brand 色（Navy/Gold） | ✅ 刻意品牌決定 | 不為模仿換紫色 |

---

## 8 個值得借的（按 CP 值排）

### 🔴 1. KPI 矩陣（ROI / XIRR × 1Y / 3Y / 累積）
**Effort**: 半天-1天
**為什麼**: 這是 portfolio analytics 的核心數字。「我這檔到底賺多少%」是使用者每次回來看 portfolio 第一個問題。Assetra 現在連看都看不到。

**實作**:
- Asset-level KPI computed in domain service (從 trade history + close price)
- Side panel 加一個小 grid (2 rows × 3 cols)
- Cell color: positive = AppUp, negative = AppDown
- 「累積」 = 從第一筆 buy 至今的 ROI / IRR

### 🔴 2. 已實現損益 breakdown
**Effort**: 半天
**為什麼**: 股息 vs 價差是兩種不同的收益語意，混在「總損益試算」失去資訊。報稅 / 評估投資效率都需要分開看。

**實作**:
- 「總已實現 = 股息 + 價差」 三行
- Asset-level realized PnL 服務（從 trade history sum）
- 已有 Cash Dividend / Stock Dividend trade type → 資料來源齊全

### 🟡 3. Inline 價格圖
**Effort**: 1-2 天
**為什麼**: 「最有 wow factor」widget。讓 panel 從「數字 dashboard」升級成「視覺分析」。

**實作**:
- LiveChartsCore line series (跟 PortfolioHistoryViewModel 同套)
- Time selector chip (5D / 1MO / 6MO / 1Y / 5Y / MAX)
- 「價格」(close price history) / 「我的價值」 (market_value = qty × price) toggle
- 需要：history price API（Stock service）+ asset-level snapshot 整合

### 🟢 4. 多 tag chip（asset class + currency）
**Effort**: 30 min
**為什麼**: 純視覺一階升級，工程量 trivial。

**實作**:
- side panel header 下方加 chip row
- chip 1: asset class（股 / ETF / 基金 / 加密…）
- chip 2: currency code（TWD / USD）— 用 currency-specific color
- 既有 badge 樣式擴充即可

### 🟢 5. 2×2 大卡 layout（從 2×4 收成 4 張）
**Effort**: 1-2 hr
**為什麼**: 焦點集中。當前 7 張小卡資訊密度高但「沒主角」。

**實作**:
- 重排 metrics 成 4 個 thematic：
  - 現價 / 均價 (含「成交均價」副字)
  - 持有數量
  - 市值 / 成本
  - 總損益（含 % + 絕對值）
- 每張 card padding bump

### 🟢 6. 主 CTA 集中
**Effort**: 30 min
**為什麼**: 一個明確的 primary action 比兩個並排按鈕更易掃描。

**實作**:
- 「+ 新增紀錄」 single button (primary color)
- Click → dropdown：「買入 / 賣出 / 現金股利 / 股票股利 / …」
- 等效於現在已有的 quick add menu，移到 panel 內

### 🟢 7. Selected card 高亮
**Effort**: 30 min
**為什麼**: 互動感 + 讓 4 大 metric 之一變成「主視角」。

**實作**:
- Trigger on click → background = AppAccentSubtle, border = AppAccent
- 預設選「市值 / 成本」（最常看的 metric）

### 🟢 8. Pill tab style
**Effort**: 30 min
**為什麼**: 純視覺風格選擇。Pill 看起來更現代，underline 比較傳統。

**實作**:
- 修 panel 內 tab style: AppSubTab → 新加 AppPillTab variant
- Active: filled bg + white text
- Inactive: transparent bg + secondary text

---

## 不該做的（避免錯方向）

| 項目 | 為什麼別做 |
|------|------------|
| 換 primary color 成紫色 | Brand Gold/Navy 已定，跟 mockup 一致；換紫色等於砍掉品牌 identity |
| NavRail 改 flat | Assetra 4-group hierarchy 是 Wealth Management 思維，比 AscentPortfolio 強 |
| 砍 status bar | Assetra 三重資訊比 AscentPortfolio 多，是價值點 |
| 全套抄 AscentPortfolio | 它沒做的東西（多幣別、複委託、跨頁 integration）是 Assetra 的核心優勢 |
| 把 panel layout 改成 100% AscentPortfolio | 對 7 個 metric 重排成 4 個會丟資訊；應該是「保留 7 個但分主從層次」而不是「砍掉 3 個」 |

---

## 建議執行順序

如果要動工，推薦：

| Phase | 內容 | 累積時間 |
|-------|------|----------|
| **P4.1** | #1 KPI 矩陣（ROI / XIRR）— 真正資訊價值 | 半天 |
| **P4.2** | #2 已實現損益拆解（股息/價差）— 補資訊缺口 | 半天 |
| **P4.3** | #4 + #7 + #8 + #6 純視覺 polish 一次做完 — 4 個小項 | 半天 |
| **P4.4** | #5 layout 重排 2×4 → 2×2 視覺主從 | 1-2 hr |
| **P4.5** | #3 Inline 價格圖（最有 wow but 工程量最大） | 1-2 天 |

**Total**: 約 3-4 天 if 全部做。

**最小可上**: P4.1 + P4.2 = 1 天 = 把 Assetra 從「能用」升到「跟 AscentPortfolio 同級資訊深度」。

---

## 觸發條件

User 決定動 → 一個 phase 一個 commit + 這份 MD 標進度。

不要 proactive 開始，等 user 明確說「動 P4.1」之類才動。

---

## 進度

| Phase | 狀態 | 備註 |
|-------|------|------|
| P4.1 | ✅ done | KPI 矩陣 ROI/XIRR × 1Y/3Y/累積；ViewModel inline 計算 + asset detail XAML 4×3 grid |
| P4.2 | ✅ done | CapitalGain 實作（Sell.RealizedPnl 加總）+ Realized total / 資本利得 兩列改 signed + 依符號染色 |
| P4.3 | ✅ done | 3/4 子項：#4 幣別 chip（TWD 灰 / USD 紫）、#7 市值 card accent 提升為 primary metric、#8 panel tab 改 pill style（新增 AppPillTab）。#6 CTA dropdown 跳過——既有 3 按鈕已 secondary 風格、Popup 化複雜度與價值不平衡 |
| P4.4 | ✅ done | Stats grid 從 2×4（7 張小卡 + 滿版 Pnl）重排為 2×2 thematic：① 持有數量 ② 現價/均價（含 成交均價 sub-sub）③ 市值/成本（primary accent，含 淨值 sub-muted）④ 總損益（% sub + 預估賣出費 muted）。每張 padding 16,14 比原本 14,12 略 bumped。原本各獨立 card 的 cross-currency / day-change / quote-stale 都保留為 sub-row 不丟資訊 |
| P4.5 | ✅ done | Inline 價格圖 — `PortfolioViewModel.AssetChart.cs` 新 partial 抓 `IStockHistoryProvider` close price，繪 LiveCharts line series；4 個 period chip（1月 / 3月 / 1年 / 2年，對應既有 `ChartPeriod` enum）+ 價格/我的價值 模式 toggle；loading / empty state 完整。**範圍縮減**：MD 原規格 5D/6MO/5Y/MAX 因會改 `IStockHistoryProvider` enum + 5 個 provider 實作非 surgical，留作後續擴充。「我的價值」用 **當前**持倉數量乘歷史價（為 MVP 簡化；要精準回放需走 trade journal） |
| P4.6 | ✅ done | UI polish 補課：① panel 寬度 `0.44/420/620` → `0.52/480/760`；② Stats grid 頂列 swap（現價/均價 左、持有數量 右，對齊 AscentPortfolio）；③ #6 主 CTA — tab bar 右側加 primary "+ 新增交易" Button + ContextMenu（買入 / 賣出 / 配息入帳），移除原本 overview tab 內 3 顆 AppSecondaryButton 橫排。Code-behind 加 1 個 Click handler 打開 ContextMenu，其餘走純 XAML |
| P4.7 | ✅ done | 擴充 `ChartPeriod` enum 加 4 個視窗：`FiveDays / SixMonths / FiveYears / Max`。5 個 provider 全跟進：`YahooFinanceHistoryProvider` 對應 range `5d/6mo/5y/max`、`CachedStockHistoryProvider` 對應 date 換算（Max ≈ 10Y）、`FinMindHistoryProvider` 與 `TwseHistoryProvider` 對應 months（5D 退回 1 月，Max = 120 月）。Asset chart UI 對應 7 個 chip（5D / 1M / 3M / 6M / 1Y / 5Y / MAX）；5D 視窗加 client-side filter 砍到最近 7 個日曆日讓 chip 標籤誠實（Twse/FinMind 不支援 < 1 月） |
| P4.8 | ✅ done | 「我的價值」模式改走 trade journal 重播：`BuildMyValuePoints` 跑 Trades collection 計每筆 Buy / Sell / StockDividend 的 ΔQty，merge-sort 跟 OHLCV 兩 pointer 走一遍得到每日歷史持倉，再乘 close 得到當日市值。Qty=0 的 OHLCV 點略過 → chart 起點自然落在第一筆 Buy 日。`NotifyTradeDependentDetailPropertiesChanged` 加 chart reload 觸發 |
| P4.9 | ✅ done | ① 三個 detail panel 寬度統一為 `0.55/520/820`（Position 從 `0.52/480/760` 加寬、Cash 與 Liability 從 `0.44/420/620` 加寬到一致）；② Position panel 交易紀錄分頁重設計成 table-style 橫向列：移除上方冗餘 MarketValue / Pnl 兩張 KPI 卡（已在 overview tab）、6 欄 Grid header（日期 / 類型 / 數量 / 單價 / 價值 / 備註）+ 每列同欄寬，價值欄 sub-row 顯示 `(手續費: X.XX)`，Note 改 ToolTip 顯示；對齊 AscentPortfolio VT 截圖風格 |
| P4.9b | ✅ done | Cash + Liability detail panel 交易紀錄分頁同樣 table-style 改造：4 欄 Grid（日期 / 類型 / 價值 / Edit），column header + bottom border 1px AppBorderLight 分隔，Note 改 ToolTip。Cash 用 `TotalAmount` 帶 sign coloring（流入 AppUp / 流出 AppDown），Liability 沿用既有 `CashAmount + signed-dash` 不換語意（保守 — sign convention 需逐筆驗證再決定） |
| P4.9c | ✅ done | 三個 panel 都拿掉 Edit pencil icon 欄位與 header 「備註」欄；改為整列點擊觸發 `Transaction.EditTradeCommand`：Border `Cursor=Hand` + `MouseBinding LeftClick` + IsMouseOver style trigger 套 AppHover 背景。Position 從 6 欄縮成 5 欄、Cash / Liability 從 4 欄縮成 3 欄；Note 仍透過 row ToolTip 顯示 |
| P4.9d | ✅ done | 編輯紀錄 dialog 兩個 TextBox 游標 bug：① `AppTextBox` template placeholder 改為 `Text==""` AND `IsKeyboardFocused==False` 才顯示 — 點進去 placeholder 立刻消失、caret 獨立可見；② `ThousandSeparatorBehavior` + `AppNumberTextBox` 補 `TextAlignment=Right` 讓 caret 跟 placeholder 同對齊（原本只設 `HorizontalContentAlignment` 只搬 placeholder） |
| P4.9e | ✅ done | Cash + Liability detail panel 對齊 Position 風格：① subtitle 副標、② 幣別 chip（TWD slate / USD violet）、③ tab bar 換 `AppPillTab` style + DockPanel layout、④ tab bar 右側加 primary "+ 新增交易" CTA + ContextMenu（Cash：收入 / 存入 / 提款 / 轉帳；Liability 依 IsLoan 顯示借款 / 還款，IsCreditCard 顯示消費 / 繳款）。新增 2 個 VM RelayCommand `BeginTxForSelectedCash(string)` / `BeginTxForSelectedLiability(string)`，預填 TxType 與帳戶 / 貸款標籤；MainWindow.xaml.cs 既有 `AddMenuButton_Click` 重用做 popup 開啟 |
| P4.9f | ✅ done | 三個 detail panel 的 backdrop 統一掛 `AppDialogOverlayBorder`（`Brush.ModalOverlay` dim scrim：light=#66黑 / dark=#99黑）。一度短暫換成透明 backdrop，但 user 反映黑色遮罩比較好（聚焦感），revert 回 dim 並把過渡用的 `AppSidePanelBackdropBorder` style 刪除收尾。三個 panel 從此 backdrop 一致 |
| P4.9g | ✅ done | Cash + Liability backdrop 之前 dim 連 navrail 都蓋（structural：overlay 直接掛在 MainWindow Row=1 跟 NavRailView 同層、HorizontalAlignment=Stretch），跟 Position panel 不一致（Position overlay 在 PortfolioView 內、navrail 不受影響）。修法：① NavRailView code-behind 加 `NavPaneWidth` read-only DP 追蹤 NavPane.ActualWidth（200 expanded ↔ 56 collapsed，SizeChanged 同步），② 新 `DoubleToGridLengthConverter` 處理 double → GridLength（WPF binding 不自動 coerce），③ MainWindow 兩個 overlay outer Grid 加 2-column 結構，Col 0 寬度 binding 到 ShellNavRail.NavPaneWidth、Col 1 = star，overlay 內容掛 Grid.Column=1。三個 panel 自此 dim 只蓋頁面內容、navrail 永遠保留可見可點 |
| P4.9h | ✅ done | P4.9g 的後遺症：navrail 可點 = panel 會 leak 到下個 section（user 切 nav 但 panel 還顯示）。修法在 `MainViewModel` ctor 訂閱 `NavRail.PropertyChanged`，section 一變就把 `Portfolio.SelectedPositionRow / SelectedCashRow / SelectedLiabilityRow` 全 null 掉 — `HasSelectedXxxRow` 跟著 false → 3 個 panel 都關。一致行為：每次切 nav 都是乾淨的 page context（後續 P4.9i 取代） |
| P4.9i | ✅ done | User 指出 Position panel 結構是頁面內（PortfolioView 裡），Cash/Liability 是 shell-level，建議統一成 Position 那種頁面內。把 shell-level Cash overlay (~530 行) 搬進 `AccountsTabPanel.xaml`、Liability overlay (~720 行) 搬進 `LiabilityTabPanel.xaml`，Grid.RowSpan=4 + Panel.ZIndex=180 + 既有 Visibility binding。AccountsTabPanel 同時在 Portfolio 子分頁與 CashAccountsView 中渲染，所以 cross-page click 行為保留。**反向退掉 P4.9g 全套**（`NavPaneWidth` DP、`DoubleToGridLengthConverter` 整檔刪除、App.xaml 註冊、MainWindow column wrapper、ShellNavRail x:Name）+ **退掉 P4.9h 整段** (`NavRail.PropertyChanged` 訂閱不再需要 — 結構自動 scope，no leak)。MainWindow.xaml 從 3526 行縮到 2249 行（淨刪 1277 行），整個 sprint 淨 -68 行（structural fix 比 patch 更精簡）。三個 panel 結構自此完全對稱：都在自己的頁面組件內、unload 即消失、navrail 自然不被覆蓋 |
| P5.1 | ✅ done | ① 移除「重算這天」按鈕（partial-price snapshot 假象的 workaround）— TrendsView footer 工具列、PortfolioHistoryViewModel.RepairDate command、`_maintenance` 注入、6 個 lang key (Trends.Repair.*) 全部刪除。② 資產趨勢曲線改算真實淨值 — `ConvertMarketValueToBaseAsync` 改用 `ResolveNetWorthValue`（與 InvestmentFocusWidget 同公式）：v0.30+ snapshot 用 `Cash + Equity − Liability`、舊版 fallback `MarketValue`。partial-price 假象在新公式下自動消失（Cash/Liability 不跳）。③ Navrail「規劃」群組 icon 從 `Compass24` 改 `Rocket24` — user 反映原本跟下方「工具」群組的 `Wrench24` 視覺混淆，Rocket 強調「前瞻/規劃」語意，跟扳手清楚區隔 |
| P5.2 | ✅ done | 交易記錄 card 三個視覺問題：① 拿掉 `<Button AppSecondaryButton>` wrapper、改用單一 Border + Cursor=Hand + MouseBinding（對齊 P4.9c 列點擊 pattern）讓 card 自然 stretch；② DockPanel 中段 name TextBlock 加 `Margin="12,0,0,0"` 跟 date 有 12px 喘氣空間，所有 inline 元件補 VerticalAlignment=Center；③ hover 從 AppSecondaryButton 強烈藍邊改 Border IsMouseOver trigger 套 AppHover brush；card padding 14,12 → 14,10，card 間距 10 → 8，WrapPanel 上邊距 10 → 8 — hover 像「正在選的列」而非「按下的按鈕」 |
| P5.3 | ✅ done | 全頁面內容區統一加 `24,16,24,0` padding。FinancialOverview 在 P3.8 加過於 TabControl，但其他 14+ 頁面（投資 / 帳戶 / 負債 / 訂閱排程 / 警示 / 收支分類 / 交易記錄 / 匯入 / 設定 / 助手 / Goals / FIRE / Monte Carlo / 不動產 / 保險 / 退休 / 實物）都沒有。修法：`NavRailView.xaml` `ContentControl`（所有頁面 template 共同 parent，line 715）一處設 Padding，全頁面同步取得 24px 左右 + 16px 上 breathing；同時拿掉 FinancialOverview `TabControl` 重複的 Margin 避免疊成 48,32 |
| P5.4 | ✅ done | P5.2 用 `ItemContainerStyle="{x:Null}"` 沒徹底解決問題 — WPF 仍 fallback 套基本 Template，點選/hover 還是顯示藍邊框+藍底穿透；ListBox 又沒設 `HorizontalContentAlignment=Stretch`，item 縮成 content 自然寬度。雙重修正：① `HorizontalContentAlignment="Stretch"` 直接設在 ListBox 上強制 item 橫向 fill row；② 完全自訂 `ListBox.ItemContainerStyle`（Padding=0 / Margin=0 / Background=Transparent / BorderThickness=0 / Focusable=False）+ 純 `ContentPresenter` Template，DataTemplate 的 Border 直接是視覺主體、hover 只走 Border IsMouseOver trigger；③ ListBox 自身 Margin 12 → 0,12,0,0 避免跟 P5.3 的 padding 24,16 疊 |
| P5.5 | ✅ done | 跨幣別 hint 誤觸發 bug：選 DRAM (USD ETF) + 幣別 dropdown 改 TWD + 富邦 TWD 帳戶 → 按確認噴「跨幣別買入請填寫匯率」。Root cause：`Buy.InstrumentCurrency` 只跟 `AddSymbolCurrency` 同步（永遠 USD），用戶手動改 `TxCurrency` 沒傳遞到 Buy 層，`ResolveInstrumentCurrencyForBuy` 永遠拿 USD vs TWD 比 → `IsCrossCurrency` 永遠 true。雙修：① `OnTxCurrencyChanged` 同步寫 `Buy.InstrumentCurrency = value.ToUpperInvariant()`；② `ResolveInstrumentCurrencyForBuy` 優先讀 `BuyContext.InstrumentCurrency`，沒值才 fallback `AddSymbolCurrency`。擴 `IBuyExecutionContext` 加 `InstrumentCurrency` 介面屬性（default `""`，向後相容） |
| P5.6 | ✅ done | 複委託情境（USD 標的 + TWD 扣款）使用者必須自行算 47500/1500.5 才填得出 FxRate — 券商明細給的是 TWD 扣款數字。原 P3 把 `ActualCashAmount` 欄位從 dialog 拿掉、理由是「未來會用資產級設定」但那條路徑沒實作，user 卡住。修法：`BuyTxForm.xaml` 在 FxRate 欄位下方再加 `ActualCashAmount` 欄位（只在 `Buy.IsCrossCurrency=true` 顯示）。Validation 從 P3 起就是「FxRate 或 ActualCashAmount 擇一」，backend 完全沒動、純恢復 UI 接點 |
| P5.7 | ✅ done | 跟使用者討論後落地 Design B — 三個跨幣別 form (Buy / Sell / CashDividend) 統一改：① 縮短跨幣別 banner（兩行短文案，新 lang key `Portfolio.Tx.CrossCurrency.Banner.Short` / `Hint.Short`）；② `ActualCashAmount` promoted 為主欄位（Buy 從 FxRate 後 → 移到 FxRate 前），label 旁加紅徽章「複委託請填」(Visibility 跟 IsCrossCurrency 走，新 lang key `Portfolio.Tx.ActualCashAmount.RequiredBadge`)；③ FxRate 收進 `AppExpander`「進階」摺疊區（IsExpanded=False，新 lang key `Portfolio.Tx.Advanced`）；④ 新 brush `AppDangerSubtle`（Light=#FEF2F2 / Dark=#33EF4444）給徽章背景，跟 `AppAccentSubtle` 同層級 |
| P5.8 (Codex) | ✅ done | Codex commit `03a0493` 重構 Buy 的 cross-currency settlement section：`SettlementInputMode`（statement vs fx）顯式 toggle + `SettlementPairDisplay` + `FxRateDate` + `FxSourceLabel` + `IsFxManual` + `FxFetchError` + 接 `TransactionFxRateResolver` 的 `FetchBuyFxRateCommand`。**只動 Buy**，Sell + Dividend 留在 P5.7 banner+Expander 架構 → 觸發 P5.8a propagate |
| P5.8a | ✅ done | propagate Buy 的 settlement architecture 到 Sell + Dividend，**先求結構一致**，為 P5.8b 抽 `CrossCurrencyOverlay` 共用 component 鋪路。**不動 Confirm path**（backend Sell / Dividend 邏輯沒變、tests 不會回歸）。VM 改動：mirror `BuyTxViewModel` 加 `SettlementInputMode` / `SettlementCurrency` / `FxRateDate` / `FxSourceLabel` / `IsFxManual` / `FxFetchError` + computed display + `NormalizeSettlementCurrency()` + `Reset()` 補清。XAML 改動：mirror BuyTxForm cross-currency 區塊（header + pair badge + segment toggle + statement vs fx 子面板），GroupName 各自取 `TxSellSettlementInputMode` / `TxDivSettlementInputMode` 避免 RadioButton 串組。**刻意暫缺**：① `FetchSellFxRateCommand` / `FetchDividendFxRateCommand` 沒接（一鍵抓匯率僅 Buy 有）；② Confirm path 仍走既有 ActualCashAmount-or-FxRate 邏輯沒做 mode-aware 分支 — 進 P5.8b 之前先補。Test 同步更新（commit `38e1ab1`）：移除「Info badge 必須存在」斷言、保留「不可有 danger 色系背景」、新增「Sell + Div 都用 SettlementInputMode toggle」鎖定新架構 |
| P5.8b | ⏸ deferred | 抽 `CrossCurrencyOverlay` 共用 UserControl。**hold 中**：P5.8a 完成資料層鏡像但沒接 fetch 行為；三 form 還沒結構對稱。動 P5.8b 兩個前置條件：① Sell + Dividend 各自 `FetchFxRateCommand` 接 resolver、② Confirm path 走 mode-aware 分支 |
| P5.9 | ✅ done | `TrendsView` footer 7-chip period selector 重排：「5天 / 1個月 / 6個月 / 本年迄今 / 1年 / 5年 / 全部」。原只有 1M / 3M / 1Y / 2Y / All。`PortfolioHistoryViewModel` 加 `_activePeriodKey` 欄位 + `ChangePeriod` 的 YTD 分支：YTD 換算 `SelectedDays = (DateTime.Today − new DateTime(Today.Year,1,1)).Days + 1` 後跑 `RefreshChartAsync`；其他 chip 走既有 `SelectedDays` mapping。新 lang keys：`Trends.Period.5D` / `Trends.Period.YTD` |
| P5.10 | ✅ done | 配置概覽每列改用 donut indicator 替代橫向 bar。新 component `DonutPercentBadge.xaml` (44×44 UserControl，2 個 stroked Ellipse：background ring `AppBorderLight` + foreground arc `StrokeDashArray` driven by Percent DP，`StrokeDashCap=Round` + `RotateTransform −90°` 讓 0% 起點在 12 o'clock)；3 個 DP (`Percent` / `FillBrush` / `PercentText`)。新 converter `PercentToDashArrayConverter` (Percent 0~100 → `DoubleCollection { filled, gap }`，`ConverterParameter` = 圓周總 stroke-dash 單位 28.3，對應 radius 18 / strokeThickness 4)。`AllocationView` 每列從「色點+名稱+值 / bar / 損益」3-row 改「donut badge / 名稱+值上方 / 損益下方」2-col 2-row 結構，整體 diff -403 / +150 = 淨 -253 行 |
