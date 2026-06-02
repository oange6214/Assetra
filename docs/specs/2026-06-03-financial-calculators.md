# 理財試算（Financial Calculators）— 設計規格

- **日期**：2026-06-03
- **狀態**：設計已核准，待轉實作計畫
- **範圍**：Assetra Phase 1「理財型計算機組」

## 1. 目標與定位

Assetra = 個人理財／資產管理／淨值規劃。本功能新增一組**純試算計算機**，協助使用者在重大理財決策前先算清楚。符合 Assetra「理財型」核心定位，且**零持倉資料依賴**（不碰投資/負債資料）→ 風險最低、對新手最有感。

明確**不屬於** Stockra 的選股/預測/策略範疇（遵守 `CLAUDE.md`：不加 AI／選股／自訂策略）。

## 2. 範圍

一個可導航功能「理財試算」，置於側欄「規劃」群組，內含 **TabControl 4 分頁**：

1. 貸款計算
2. 拿鐵因子
3. 72 法則
4. 租房 vs 買房（務實中等）

**無狀態**：不存 DB、不記憶上次輸入、不接持倉/負債。比照 MonteCarlo。

### 非目標（YAGNI）
- 不存檔／不記憶輸入
- 不接現有持倉/負債資料自動帶入
- 租 vs 買**不含**：頭期機會成本、房貸利息抵稅、買賣交易成本（屬「完整」級，未來再升）
- 貸款**不含**：寬限期、額外還款、利率變動

## 3. 架構（沿用 MonteCarlo / Import 既有模式）

三層分離：

| 層 | 路徑 | 內容 |
|---|---|---|
| **Core** | `Assetra.Core/Models/Calculators/` | 各計算機 input/result `record`（decimal、不可變） |
| **Application** | `Assetra.Application/Calculators/` | 純計算 service（無 UI 依賴）。複用 `GoalPlanningService`（複利 FV/PMT）；新寫 `LoanAmortizationService`（現有無攤還表） |
| **WPF** | `Assetra.WPF/Features/Calculators/` | `CalculatorsViewModel`（父，持 4 子 VM）＋ 4 子 View/VM；`CalculatorsView` = TabControl |

### 導航接線（新增功能標準步驟）
- `NavSection.Calculators`（enum）
- `MainViewModel.Calculators`（屬性，建構式注入）
- `NavRailViewModel.BuildGroups()` 規劃群組加一條（圖示 `Calculator24`、`LabelResourceKey = "Calc.Title"`）
- `NavRailView.xaml`：xmlns、`CalculatorsContentTemplate`、`ActiveSection` DataTrigger
- DI：`AddCalculatorsContext()` → `AppBootstrapper`
- 錢欄位掛 `ThousandSeparatorBehavior`；幣別跟隨 App 設定

## 4. 各計算機規格

### 4.1 貸款計算
- 輸入：本金 `P`、年利率 `a`、期數 `n`（月）
- 月利率 `r = a / 12`
- 月付 `PMT = P·r(1+r)ⁿ / ((1+r)ⁿ − 1)`；`r = 0` 退化為 `P/n`
- 輸出：月付、總還款 `= PMT·n`、總利息 `= 總還款 − P`、**攤還明細表**（每期：期初餘額、月付、本金、利息、期末餘額）
- 新寫 `LoanAmortizationService`；decimal 為主，冪次運算注意精度與末期餘額歸零

### 4.2 拿鐵因子
- 輸入：每筆金額 `c`、頻率（日/週/月）、年報酬率 `a`、年數 `y`
- 月投入換算：日 `×365/12`；週 `×52/12`；月 `×1`。月利率 `r = a/12`、期數 `m = y·12`
- `FV = PMT_m·[((1+r)^m − 1)/r]`；`r = 0` → `PMT_m·m`
- 輸出：累積投入 `= PMT_m·m`、複利後總值 `FV`、多賺 `= FV − 投入`
- 複用 `GoalPlanningService` FV 邏輯（必要時抽共用 helper）

### 4.3 72 法則
- 雙向：給年報酬率 `a(%)` → 翻倍年數 `≈ 72/a`；給目標年數 `y` → 所需年報酬率 `≈ 72/y`
- 附小表：翻 2/4/8 倍年數
- 標示為教育性近似；可選顯示精確值 `ln2 / ln(1+a)`
- 純算術，不另立 service（小 helper 或 VM 內）

### 4.4 租房 vs 買房（務實中等）
- 輸入（買）：房價 `H`、頭期 `D`（%或額）、房貸年利率、貸款年限 `L`、年持有成本率 `m%`（稅+管理+修繕）、年增值率 `g`
- 輸入（租）：月租 `R`、年租金漲幅 `ri`
- 輸入（共通）：比較年數 `N`
- 計算（至第 N 年）：
  - 貸款額 `= H − D`；用 `LoanAmortizationService` 取月付與第 N 年底剩餘餘額 `B_N`
  - 買方累積現金支出 `= D + Σ(月付×12, 至 min(N,L)) + Σ(H·m, N 年)`
  - 期末房產淨值 `= H·(1+g)^N − B_N`
  - 買方淨成本 `= 累積現金支出 − 期末房產淨值`
  - 租方淨成本 `= Σ_{k=0..N-1}(R×12×(1+ri)^k)`
- 輸出：N 年後買/租各自淨成本、誰划算、**損益兩平年**（買方淨成本首次 ≤ 租方淨成本的年）
- 複用 `LoanAmortizationService`

## 5. 資料流與錯誤處理（比照 MonteCarlo）

輸入（string，`PropertyChanged` 綁定）→ `Calculate` 指令 → `ParseHelpers.TryParse*` 逐欄驗證 → Application service 純計算 → result record → VM 屬性 + `ObservableCollection`（表格）→ View（卡片 + DataGrid）。

- 驗證失敗：設 `ErrorMessage`（雙語，`Calc.*.Error.*`）、`HasResult = false`、不顯示結果區
- 邊界：利率/年數 0 或負、期數非正整數 → 明確錯誤訊息

## 6. 測試策略（Rule 9，目標 80%+）

重點在 Application service（純函式、決定性）：

- **貸款**：已知輸入的 PMT 對標公式；攤還表本金加總 = 本金；末期餘額 ≈ 0；總利息 = 總還款 − 本金；`r=0` 退化
- **拿鐵**：年金 FV 對標；頻率換算正確；`r=0` 退化
- **72 法則**：雙向值；翻倍表
- **租 vs 買**：損益兩平年正確；N 年買/租淨成本；`g=0`、利率=0 邊界
- **VM**：壞輸入 → `ErrorMessage` 且無結果；正常 → `HasResult` + 數值

測試需編碼 WHY（每個 assert 綁一條業務語意，例如「攤還表本金加總必須等於本金，否則攤還邏輯錯」）。

## 7. 在地化

雙語檔（`zh-TW.xaml` + `en-US.xaml`）皆加 `Calc.*` keys：

- `Calc.Title`、`Calc.Tab.{Loan|Latte|Rule72|RentVsBuy}`
- 各 `Calc.{Calc}.Input.*` / `.Result.*` / `.Error.*`

View 用 `{DynamicResource …}`；VM 錯誤訊息用 `ILocalizationService.Get(key, fallback)`。

## 8. 驗收標準

- 側欄「理財試算」可進入，4 分頁切換正常
- 4 個計算機輸入 → 計算 → 正確結果，貸款/租vs買含表格
- 壞輸入有雙語錯誤、不崩潰
- 全部 Application service 有單測且綠燈；專案 `TreatWarningsAsErrors` 通過、`dotnet format` 乾淨
- 雙語檔同步、無遺漏 key

## 9. 檔案清單（預估）

- **Core**：`LoanAmortizationInputs/Schedule/Row`、`LatteFactorInputs/Result`、`RuleOf72Inputs/Result`、`RentVsBuyInputs/Result`
- **Application**：`LoanAmortizationService`、`LatteFactorCalculator`、`RuleOf72Calculator`、`RentVsBuyCalculator`
- **WPF** `Features/Calculators/`：`CalculatorsView(+VM)`、`LoanCalcView(+VM)`、`LatteFactorView(+VM)`、`RuleOf72View(+VM)`、`RentVsBuyView(+VM)`、`CalculatorsServiceCollectionExtensions`
- **接線**：`NavSection`、`MainViewModel`、`NavRailViewModel`、`NavRailView.xaml`、`AppBootstrapper`
- **語言**：`zh-TW.xaml`、`en-US.xaml`
- **測試**：各 service + 各 VM
