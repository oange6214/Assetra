# Docs Gap Review

> 日期：2026-04-27  
> 範圍：`README.md`、`docs/architecture/*`、`docs/planning/*` 與目前 `Assetra` 實作狀態對照  
> 目的：釐清文件是否與現況一致、哪些描述已超前實作、哪些能力仍屬 MVP

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

### 1. `Goals` 在文件中比實作更成熟

#### 文件描述
在技術藍圖中，`Goals` 被描述成完整子系統，包含：
- `GoalPlanningService`
- `GoalProgressQueryService`
- `GoalFundingWorkflowService`
- milestone / funding rule 等延伸模型

#### 實際狀態
目前實作為：
- `FinancialGoal`
- `GoalSqliteRepository`
- `GoalsView / GoalsViewModel`
- 基本進度與期限顯示

尚未看到：
- 獨立 `Assetra.Application/Goals`
- funding rule
- milestone
- 完整 query / workflow service 切分

#### 判定
**目前是 Goals MVP，不是完整 Goals 子系統。**

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

尚未實作的是：
- 資產負債表
- 現金流量表
- 損益表
- 報表匯出 pipeline

#### 判定
**目前是月結報告 MVP，不是完整 Reports 子系統。**

---

### 3. `Trends` 已可用，但仍是第一階段版本

#### 文件描述
文件已將淨資產趨勢列為 v0.6.0 主要成果。

#### 實際狀態
目前已實作：
- 折線圖
- 30 / 90 / 180 / 365 / All
- 自訂日期範圍

尚未實作：
- 類別堆疊圖
- 重大事件標註
- 更完整分析層

#### 判定
**目前是 Trends MVP。**

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

## 四、目前真正還缺少的能力

以下能力在 docs 中有規劃，但目前尚未完整落地：

### 1. 匯入治理（Importing / Data Governance）  *(v0.7.0：MVP 完成)*

v0.7.0 完成了 CSV / Excel 匯入 MVP，覆蓋台股 Top 5 銀行（國泰世華 / 玉山 / 中信 / 台新 / 富邦）與 Top 5 券商（元大 / 富邦 / 凱基 / 永豐金 / 群益）。

已落地：
- Core 模型：`ImportBatch` / `ImportConflict` / `ImportPreviewRow` / `ImportFormat` / `ImportApplyOptions` / `ImportApplyResult`
- 介面：`IImportFormatDetector` / `IImportParser` / `IImportConflictDetector` / `IImportApplyService`
- Application：`ImportConflictDetector`、`ImportApplyService`、`ImportMatchKey`
- Infrastructure：`ImportFormatDetector`、`ConfigurableCsvParser` / `ConfigurableExcelParser`（由 `CsvParserConfigs` / `ExcelParserConfigs` 宣告式驅動，新增格式只需改 config）、`ImportParserFactory`
- WPF：`Features/Import/ImportView` + `ImportViewModel`（拖放、自動偵測、預覽 grid + 每列 Resolution 下拉、現金帳戶必選 + Apply）

仍未完整：
- `ImportRule`（自動分類 / 自動套用備註）
- 匯入歷史紀錄與 rollback
- reconciliation（對帳）
- PDF / OCR 匯入

判定：
- **MVP 完成**：CSV / Excel 主流程、去重、衝突確認、套用全鏈路可用
- **完整 importing 子系統未完成**：對帳、回滾、自動規則、PDF/OCR 仍待後續版本

---

### 2. 投資績效分析
尚未看到完整落地：
- XIRR
- TWR / MWR
- benchmark 對比
- 損益歸因

這仍屬 roadmap 內的未完成功能。

---

### 3. 完整報表與匯出
尚缺：
- 資產負債表
- 現金流量表
- 損益表
- PDF / CSV export

---

### 4. Goals 完整化
尚缺：
- milestone
- funding rule
- 獨立 query / workflow service
- 更完整的提醒 / 自動指派資金來源

---

### 5. 風險 / 稅務 / 多元資產 / 雲端同步
這些仍多數停留在 roadmap 階段，尚未見完整落地。

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
