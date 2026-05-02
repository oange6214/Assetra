# Docs Gap Review

> 初稿日期：2026-04-27；最後更新：2026-04-28（v0.21.1）
> 範圍：`README.md`、`docs/architecture/*`、`docs/planning/*` 與目前 `Assetra` 實作狀態對照
> 目的：釐清文件是否與現況一致、哪些描述已超前實作、哪些能力仍屬 MVP

> **⚠️ 注意**：本文件 §三「文件超前於實作」與 §四「真正還缺少的能力」多數描述已在 v0.16–v0.21.1 期間出貨並過時。以下各節以刪除線標記已解決的差距；現況以括號補充說明。

## 一、總結

整體來說，`Assetra` **沒有明顯偏離文件所描述的產品方向與技術藍圖**。  
目前的差距主要不是「做錯方向」，而是：

1. 部分文件描述的是 **目標架構**
2. 部分模組目前其實只是 **MVP**
3. `README` 的版本敘事曾經比實際 GitHub release 更前

換句話說，目前最需要修正的是**成熟度表述**，而不是推翻既有 roadmap。

---

## 二、已與文件對齊的部分

以下幾條主線，文件與實作方向基本一致：

### 1. Portfolio 主線
- 投資
- 帳戶
- 負債
- 交易記錄
- 提醒
- 配置分析

對照：
- `Assetra.WPF/Features/Portfolio`
- `Assetra.Application/Portfolio`
- `Assetra.Application/Loans`
- `Assetra.Application/Alerts`

### 2. Budgeting / Recurring 已形成子系統
文件把這兩塊視為重要核心能力，這與現況一致。

對照：
- `Assetra.Core/Models/Budget.cs`
- `Assetra.Core/Models/BudgetTemplate.cs`
- `Assetra.Core/Models/ExpenseCategory.cs`
- `Assetra.Core/Models/AutoCategorizationRule.cs`
- `Assetra.Core/Models/RecurringTransaction.cs`
- `Assetra.Core/Models/PendingRecurringEntry.cs`
- `Assetra.Application/Budget/Services/MonthlyBudgetSummaryService.cs`
- `Assetra.Application/Recurring/`
- `Assetra.WPF/Features/Categories`
- `Assetra.WPF/Features/Recurring`

### 3. 趨勢 / 月結 / Goals 已有 MVP
文件中已將這些列入 v0.6.0 主線，這和現況一致，但應理解為 **MVP 已完成**，不是完整版本。

對照：
- `Assetra.WPF/Features/Trends`
- `Assetra.WPF/Features/Reports`
- `Assetra.WPF/Features/Goals`

---

## 三、文件超前於實作的地方

### ~~1. `Goals` 在文件中比實作更成熟~~ ✅ 已解決（v0.16.0）

~~在技術藍圖中，`Goals` 被描述成完整子系統…~~

**現況（v0.16.0）**：`GoalMilestone` / `GoalFundingRule` / `GoalPlanningService` / `GoalProgressQueryService` 均已落地。Goals 完整子系統已出貨，不再是 MVP。

---

### 2. `Reports` 在文件中容易被誤讀為完整報表系統

#### 文件描述
文件中已把：
- 月結報告
- 報表與輸出
- 三大報表
- PDF / CSV 匯出

放在同一條敘事線上。

#### 實際狀態
目前已實作的是：
- `MonthEndReport`
- `MonthEndReportService`
- `ReportsView / ReportsViewModel`
- `IncomeStatementService`
- `BalanceSheetService`
- `CashFlowStatementService`
- `ReportExportService`

尚未實作的是：
- 更完整的自訂期間摘要
- 更多匯出格式與報表版型
- 更進階的分析/視覺化延伸

#### 判定
**目前 Reports 子系統已基礎落地，不再只是月結報告 MVP；但仍有高階分析與更多輸出能力可擴充。**

---

### ~~3. `Trends` 已可用，但仍是第一階段版本~~ ✅ 已解決（v0.17.0）

~~尚未實作：類別堆疊圖、重大事件標註~~

**現況（v0.17.0）**：`PortfolioEvent` 事件標註與類別堆疊圖均已落地。

---

### 4. `Technical Architecture Blueprint` 屬目標架構，不是現況文件

#### 問題
這份文件內容本身沒有錯，但如果讀者把它當成「現在已經長這樣」，就會誤判專案成熟度。

#### 例子
- `Goals` 的 application-layer 拆分
- `Reports` 的三大報表與匯出層
- `Importing` 的完整 preview / dedupe / reconciliation

#### 判定
**這份文件應明確被視為中長期目標架構。**

---

## 四、出貨狀態總覽（截至 v0.21.1）

以下為 §四 原記錄的「尚缺」項目，已全數更新為出貨狀態：

### 1. 匯入治理 ✅ 完全落地（v0.7.0–v0.19.0）

- CSV / Excel 主流程、去重、衝突確認、套用全鏈路 ✅（v0.7.0）
- `AutoCategorizationRule`（手動 + 匯入共用，含 MatchField / MatchType / AppliesTo）✅（v0.8.0）
- 匯入歷史紀錄與 rollback（`ImportBatchHistory` + `ImportRollbackService`）✅（v0.8.0）
- Reconciliation 對帳工作台 ✅（v0.9.0–v0.10.0）
- PDF / OCR 匯入 ✅（v0.19.0）

### 2. ~~投資績效分析~~ ✅ 完全落地（v0.12.0）

- XIRR / TWR / MWR / benchmark 對比 / PnL 歸因 全部 ✅

### 3. ~~完整報表與匯出~~ ✅ 完全落地（v0.11.0）

- 損益表 / 資產負債表 / 現金流量表 + PDF / CSV export 全部 ✅

### 4. ~~Goals 完整化~~ ✅ 完全落地（v0.16.0）

- milestone / funding rule / GoalPlanningService / GoalProgressQueryService 全部 ✅

### 5. 風險 / 稅務 / 雲端同步 ✅ 完全落地

- 風險分析（波動率 / 最大回撤 / Sharpe / 集中度 HHI）✅（v0.13.0）
- 稅務模組（TaxSummary / 股利 / 海外所得 / 報稅匯出）✅（v0.18.0）
- 雲端同步（AES-GCM / 8 entity round-trip / manual conflict drain / GA chaos test）✅（v0.20.0–v0.21.0）

### 尚待開發（Phase 4）
- AI 財務助理（待規劃，v0.23.0+）
- 多元資產：不動產 / 保險 / 退休 / 實物 ✅ v0.22.0
- 情境模擬：FIRE / Monte Carlo ✅ v0.22.0
- 多端體驗：PWA / 行動端（v0.24.0+ / v0.25.0+）

---

## 九、Phase 4（v0.23–v0.26）文件準備清單

> 更新日期：2026-04-28。在開始 v0.22.0 實作前，確認下列文件已就緒。

### 實作前必須完成（HIGH）

| 文件 | 項目 | 狀態 |
|---|---|---|
| `Bounded-Contexts.md` | 新增 §13 多元資產（RealEstate / Insurance / Retirement / Physical） | ✅ 已完成 |
| `Bounded-Contexts.md` | 新增 §14 模擬（FIRE / Monte Carlo） | ✅ 已完成 |
| `Bounded-Contexts.md` | 更新 Context 關係圖 | ✅ 已完成 |
| `Roadmap-v0.14-to-v1.0.md` | v0.22.0 完整 task breakdown（含 UI / Reporting / Sync） | ✅ 已完成 |
| `Roadmap-v0.14-to-v1.0.md` | v0.22.0 完整 task breakdown | ✅ 已完成 |
| `Roadmap-v0.14-to-v1.0.md` | v0.22.0 完整 task breakdown（FIRE） | ✅ 已完成 |
| `Roadmap-v0.14-to-v1.0.md` | v0.22.0 完整 task breakdown（Monte Carlo） | ✅ 已完成 |
| `Technical-Architecture-Blueprint.md` | §十三 多元資產 folder 結構 + EntityVersion 規範 + BalanceSheet 擴充 | ✅ 已完成 |
| `Technical-Architecture-Blueprint.md` | §十四 Simulation 架構（純計算、Box-Muller、fan chart） | ✅ 已完成 |

### 同步更新（MEDIUM）

| 文件 | 項目 | 狀態 |
|---|---|---|
| `Architecture.md` | Application context 清單加 MultiAsset / Simulation | ✅ 已完成 |
| `CHANGELOG.md` | v0.23–v0.26 sprint 模板（規劃中） | ✅ 已完成 |

### 實作完成後補（實作各 sprint 後）

| 文件 | 項目 |
|---|---|
| `Bounded-Contexts.md` | §13 / §14 從「規劃中」改為「完成」並補主要服務名稱 |
| `CHANGELOG.md` | 各 sprint 實際出貨日期、測試數字、Git tag |
| `Assetra-Feature-Blueprint-and-Roadmap.md` | 已實作模組表補上 v0.23–v0.26 |
| `Technical-Architecture-Blueprint.md` | §十三 / §十四 更新為實際落地的 class 名稱 |
| `README.md` | Features 列表補上多元資產 + 情境模擬 |

---

## 五、最容易造成誤解的地方

### 1. `README` 版本敘事
如果 `README` 寫的是 `v0.6.0`，但 GitHub Releases 還停在 `v0.5.x`，讀者會以為：
- 已正式發布
- release 與主線完全同步

建議做法：
- 用「目前開發主線里程碑」描述
- 明確區分正式 release 與開發目標

### 2. `已完成` 不等於 `完整完成`
目前許多模組較準確的表述應為：
- `MVP 完成`
- `基礎完成`
- `第一階段完成`

而不是讓人以為：
- 所有服務層、分析層、匯出層都已齊備

---

## 六、建議文件調整原則

### 1. 在藍圖文件中區分兩種描述
- **現況**
- **目標架構**

### 2. 對已落地但仍未完整的模組使用一致標記
建議統一用：
- `MVP 完成`
- `基礎完成`
- `完整版本未完成`

### 3. 在 README 明確區分
- 正式 release
- master 開發主線
- roadmap milestone

---

## 七、目前判定

### 應用程式沒有偏離 docs 的地方
- 產品主線方向正確
- Budget / Recurring / Goals / Reports / Trends 都是沿著文件規劃前進
- 分層與 bounded context 的思路沒有明顯走歪

### 真正的差距
- 某些文件比實作成熟度更高
- 某些功能目前只有 MVP，但文件容易讓人讀成完整版本

---

## 八、結論

`Assetra` 目前的狀態可以概括成：

- **產品方向正確**
- **技術架構方向正確**
- **功能正在朝 roadmap 前進**
- **主要問題在於文件成熟度表述需要更精準**

目前最合適的說法不是：
- 「文件有問題，應用程式偏掉了」

而是：
- **「文件與實作大方向一致，但需要更清楚標明哪些是 MVP、哪些是目標架構、哪些是正式已完成能力。」**
