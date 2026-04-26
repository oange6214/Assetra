# v0.6.0 Sprint Plan — 結帳、趨勢、目標雛形

> 範圍：2–3 週。把現有 context 收尾、補關鍵視覺化，並放入 Goals MVP，組成「打開 app 就看到趨勢 + 月結 + 目標進度」的版本敘事。

## 一、版本目標

| # | 項目 | Bounded Context | 預估 |
|---|------|---|---|
| F1 | 月結報告完整 UI | Reports / Budget | M |
| F2 | 淨資產趨勢視覺化（月/季/年、堆疊圖、事件標註） | Portfolio | M |
| F3 | Goals MVP（單一目標 + 進度卡，無自動 funding rule） | Goals (新) | L |
| D1 | 動工前技術債（見 §三） | 全層 | M |

## 二、缺口全景（v0.6.0 之後的路線）

### P0（本 sprint 範圍）
- F1 月結報告 UI — `MonthEndReportService` 已有 61 行骨架，缺 query 聚合與 View binding
- F2 淨資產趨勢 — `PortfolioDailySnapshot` 資料層完成，缺時間粒度切換、堆疊圖、事件標註；要新建 `NetWorthTrendQueryService`（或擴 `PortfolioHistoryQueryService`）
- F3 Goals 子系統 — 全新 context，本 sprint 只做 MVP（`FinancialGoal` + 單頁 UI + 進度條），milestone 與 funding rule 留給 v0.7

### P1（v0.7 之後）
- Importing 治理（CSV/Excel preview、dedupe、conflict）— L
- 投資績效（XIRR / TWR / MWR）— M
- 報表 PDF / CSV export（資產負債、現金流、損益）— M
- 風險分析（波動度、最大回撤、Sharpe、集中度）— M

### P2（範圍邊緣）
- 外幣 / 美股 pipeline — L
- 稅務模組（股利、海外所得）— M
- 雲端同步 — XL

## 三、動工前要先處理的技術債

新功能不可堆在現有過載結構上。順序由低風險到高風險：

### D1-1 拆 `ServiceCollectionExtensions.cs`（330 行）
按 context 分拆成擴充方法：
- `AddPortfolioServices`
- `AddBudgetServices`
- `AddRecurringServices`
- `AddReportsServices`
- `AddAlertsServices`
- `AddLoansServices`

純機械重構，無行為變更。

### D1-2 拆 `PortfolioViewModel.cs`（2,121 行）
抽出 section sub-viewmodels（沿用現有 `SubViewModels/` pattern）：
- `NetWorthTrendSectionViewModel`（為 F2 準備）
- `AccountsSectionViewModel`
- `LiabilitiesSectionViewModel`

主 VM 只剩協調與 cross-section 邏輯。

### D1-3 補 `MonthEndReportServiceTests`
F1 上線前要先讓分類聚合 / 預算比對的 case 有測試覆蓋，避免 UI 把錯資料當對的。

### D1-4（保留，看 D1-2 後狀況）
若 Goals 也要 cross-VM refresh，把 `IBudgetRefreshNotifier` 風格統一成 `IDomainChangeNotifier<T>` 或走 messenger。

## 四、明確排除（依 CLAUDE.md）

下列 docs 中的功能 **不在 Assetra 路線圖**：
- AI 財務助理（自然語言查詢、摘要、規劃建議）
- 選股 / 策略推薦 / 新聞 ingest
- LLM-based OCR / PDF 解析

deterministic parser、雲端同步、稅務、外幣美股、報表 PDF 屬財務範疇，**保留**。

## 五、建議實作順序

1. **D1-1**（DI 拆分）— 低風險暖身，commit + CI
2. **D1-3**（補測試）— 為 F1 鋪路
3. **D1-2**（VM 拆分）— 風險最高，獨立 commit、單獨驗證
4. **F1**（月結 UI）— D1-3 已先補測試，可放心連 UI
5. **F2**（趨勢視覺化）— D1-2 已抽好 `NetWorthTrendSectionViewModel`，直接綁定
6. **F3**（Goals MVP）— 沒有相依，最後做

## 六、Definition of Done（v0.6.0）

- [ ] CI 綠
- [ ] `Assetra.Tests` 覆蓋 F1/F2/F3 的服務層 happy path
- [ ] CHANGELOG 補上 v0.6.0 條目
- [ ] `docs/architecture/Bounded-Contexts.md` 更新 Goals 標記為「已實作（MVP）」
- [ ] `docs/planning/Implementation-Roadmap.md` 勾選對應項目
