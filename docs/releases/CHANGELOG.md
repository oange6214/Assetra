# Changelog

## v0.13.2 - 2026-04-28

針對 v0.13.1 後 code review 發現的 CRITICAL / HIGH 違規做 quick fix；無新功能、行為等價，但訊號乾淨且 benchmark 可配置。

### 修正

- **Async（CRITICAL）**：`PortfolioLoadService.LoadAsync` 7 處 `Task.Result` 改為各自 `await ... ConfigureAwait(false)`；移除 CLAUDE.md 禁用模式並降低後續 refactor deadlock 風險。
- **Error handling（HIGH）**：`ReportsViewModel.LoadPerformanceAsync` 的 benchmark 空 `catch { ... }` 改為帶診斷的 `catch (HttpRequestException or InvalidOperationException or TaskCanceledException)`，並寫入 Debug log；不再吞掉 HTTP / 解析錯誤。
- **Hardcoded value（HIGH）**：benchmark 代號 `"0050.TW"` 從 `LoadPerformanceAsync` 移出，改由 `AppSettings.BenchmarkSymbol` 提供（預設 `"0050.TW"`）；空字串可關閉 benchmark 比較。`ReportsViewModel` 新增可選 `IAppSettingsService` 注入。

### 內部變更

- `AppSettings` 新增 `string BenchmarkSymbol = "0050.TW"` 欄位（向後相容；既有序列化 JSON 缺欄位時 fallback 到預設）。
- 480/480 tests 綠。

## v0.13.1 - 2026-04-28

針對 v0.13.0 release 後 code review 發現的本地化債務與文件落差做 cleanup；無新功能、無行為改變。

### 修正

- **i18n（HIGH）**：補上 Reports / Risk / Performance Expander 內所有原本 hardcode 在 `StringFormat` 的英文 label（25+ keys）；改寫為 horizontal StackPanel + DynamicResource 模式。
- **i18n（HIGH）**：Portfolio / Allocation / Rebalance / Loan / DateRangePicker / PortfolioView 「債券 ETF」徽章 + tooltip 等先前漏進 Languages 的 zh-TW 字串補進 Languages，新增對應 en-US 翻譯。
- **i18n（HIGH）**：`ReportsViewModel.ExportStatus` 訊息原為 hardcode 字串，改走 `_localization.Get(key, fallback)`，並加入對應 `Reports.Export.Status.*` keys。
- **Theme（MED）**：Risk Expander 集中度警示文字 `Foreground="#D9534F"` 改用 `{DynamicResource AppDanger}`，跟隨 theme 切換。
- **CancellationToken（MED）**：`ConcentrationAnalyzer` 的 `ct` 參數改為 propagate 至 `BuildBucketsAsync`，呼叫 repo 前後 `ThrowIfCancellationRequested`。
- **Docs（HIGH/MED）**：`Bounded-Contexts.md` 第 5 節舊 Analysis Context 描述（與第 8 節重複）改為指向 #8 的 placeholder；`Next-Sprint-v0.13.0.md` F5 `ConcentrationAlertRule` 標註 descoped、F3 risk-free rate 註明 `IAppSettingsService` 推遲到 v0.14。

### 內部變更

- 新增 ~30 個 Languages keys（zh-TW + en-US）。
- 無 schema、interface、DI 變更；行為與 v0.13.0 一致。
- 因專案內存有未提交的 Recurring/Categories WIP（與此 cleanup 無關），test project 在本地 build 失敗；本 release 僅驗證 `Assetra.WPF` 主專案 build 全綠，`dotnet test` 待 WIP 收斂後再驗證。

## v0.13.0 - 2026-04-28

風險分析：在 Analysis Context 加入波動率、最大回撤、Sharpe ratio 與持股集中度（HHI），於 Reports 頁新增「Risk Metrics」Expander 並提供集中度警示。

### 重點

- **D1 Risk DTO 與介面** — `Assetra.Core/Models/Analysis/`：`DrawdownPoint`、`ConcentrationBucket`、`RiskMetrics`（含 `HasConcentrationWarning` 計算屬性，>30% 單一部位或 HHI >0.30 觸發）；`Assetra.Core/Interfaces/Analysis/` 四個 service 介面。
- **F1 VolatilityCalculator** — 由日 value 序列算日報酬，sample std × √252 得年化波動率；少於 2 筆報酬回 null。
- **F2 DrawdownCalculator** — running peak，dd = (peak − value) / peak；輸出 `DrawdownPoint` 序列 + `ComputeMaxDrawdown` 取最大值。
- **F3 SharpeRatioCalculator** — `(annualizedReturn − riskFreeRate) / annualizedVolatility`；vol = 0 或缺值回 null。預設 rf = 0.02。
- **F4 ConcentrationAnalyzer** — Top-N + Others bucket（label 為 `Symbol DisplayName`），權重以 `PositionSnapshot.TotalCost` 為 cost-basis proxy（無同步 quote service）；HHI = Σ wᵢ²。
- **F5 集中度警示** — 改以 `RiskMetrics.HasConcentrationWarning` flag 表達（>30% 單一部位或 HHI >0.30），不擴張現有 price-target 為主的 `AlertRule` 框架；UI 直接綁該 flag。
- **WPF Risk Tab** — `ReportsView` 新增第 5 個 Expander（Volatility / MaxDD / Sharpe / HHI + 警示 + Top Holdings ItemsControl）；`ReportsViewModel` 注入 5 個可選 dep（4 service + `IPortfolioSnapshotRepository`），`LoadAsync` 之後呼 `LoadRiskAsync`。

### 內部變更

- 新增 4 個 Analysis service 至 `AnalysisServiceCollectionExtensions.cs`。
- `Languages/zh-TW.xaml` / `en-US.xaml` 新增 `Reports.Risk.Title` / `Reports.Risk.ConcentrationWarning` / `Reports.Risk.TopHoldings`。
- 466 → 478 筆測試全綠（新增 3 筆 Volatility + 3 筆 Drawdown + 3 筆 Sharpe + 3 筆 Concentration）。

## v0.12.0 - 2026-04-28

投資績效分析：在 v0.11 報表 infra 上新增 Analysis bounded context，提供 XIRR / TWR / MWR、benchmark 對比與損益歸因，於 Reports 頁多一張「Performance」報表。

### 重點

- **D1 Analysis DTO 與介面** — `Assetra.Core/Models/Analysis/`：`CashFlow`、`PerformancePeriod`（Month/Year 工廠）、`PerformanceResult`（含 Alpha 計算）、`AttributionBucket`；`Assetra.Core/Interfaces/Analysis/` 五個 service 介面。
- **F1 XirrCalculator** — Newton-Raphson（max 100 iter, tol 1e-7）+ Bisection fallback（[-0.99, 10.0]）；要求至少一筆正、一筆負流，否則回 null。
- **F2 TimeWeightedReturnCalculator** — 在每筆外部 cash flow 切 sub-period，幾何鏈接 `Π(1 + R_i) − 1`，分離資金進出對報酬率的扭曲。
- **F3 MoneyWeightedReturnCalculator** — 對 portfolio：trade journal Buy/Sell/CashDividend → cash flow，加入起 / 終 `PortfolioDailySnapshot.MarketValue` 為合成 flow，呼 XIRR；亦支援單一 `PortfolioEntryId`。
- **F4 BenchmarkComparisonService** — 透過 `IStockHistoryProvider` 拉同期 benchmark（預設 0050.TW）收盤價，計算 `(endPx − startPx) / startPx`。
- **F5 PnlAttributionService** — 拆解期間損益為四桶：Realized（Sell.RealizedPnl）、Dividend（CashDividend.CashAmount）、Commission（負）、Unrealized Δ（end−start MarketValue 扣除淨投入）。
- **WPF Performance Tab** — `ReportsView` 新增第 4 個 Expander，顯示 MWR / Benchmark / Alpha + Attribution rows；`ReportsViewModel` 注入 3 個可選 Analysis service，`LoadAsync` 自動載入。

### 內部變更

- 新增 `Assetra.Application/Analysis/`（5 個 service）+ `Assetra.WPF/Infrastructure/AnalysisServiceCollectionExtensions.cs`，於 `AppBootstrapper` 加 `AddAnalysisContext()`。
- `Languages/zh-TW.xaml` / `en-US.xaml` 新增 `Reports.Performance.Title`。
- 455 → 466 筆測試全綠（新增 5 筆 XIRR + 3 筆 TWR + 3 筆 PnlAttribution）。

## v0.11.0 - 2026-04-28

Reports MVP：以 Trade Journal 為單一事實源，提供月度損益表 / 資產負債表 / 現金流量表三大報表，並支援 PDF（QuestPDF Community）+ CSV 匯出。

### 重點

- **D1 報表 DTO 與介面** — 新增 `Assetra.Core/Models/Reports/`：`ReportPeriod`（Month/Year 工廠 + `Prior()` 等長前期窗）、`StatementRow`、`StatementSection`、`IncomeStatement` / `BalanceSheet` / `CashFlowStatement`、`ExportFormat`；以及 `Assetra.Core/Interfaces/Reports/` 四個 service 介面。
- **F1 IncomeStatementService** — 以 `Trade.Date` ∈ Period 過濾，依 `CategoryId` 聚合 Income/Expense rows（與 `ICategoryRepository` 對照取 Label，未分類顯示 `(Uncategorized)`），輸出 Income/Expense Section + Net；可選 `includePrior` 遞迴生成等長前期數據作 MoM/YoY 對照。
- **F2 BalanceSheetService** — Cash 端依 `TradeType.PrimaryCashDelta(t)` 累積（Income/Sell/CashDividend/Deposit/LoanBorrow → +；Withdrawal/Buy/Repay → −），按 `CashAccountId` 分列；可選帶入最新 `PortfolioDailySnapshot.MarketValue` 為 Investments；Liabilities 以 `LiabilityAssetId`（信用卡）+ `LoanLabel`（貸款）兩類聚合。AsOf 截止日嚴格過濾。
- **F3 CashFlowStatementService** — Operating（Income / Withdrawal / Deposit / CashDividend）、Investing（Buy / Sell）、Financing（LoanBorrow / Repay / 信用卡刷卡 / 還款）三段；Opening cash 由 pre-period trades 累積、Closing = Opening + NetChange，建構式即保證恆等。
- **F4 ReportExportService** — 共用 QuestPDF `IDocument` 模板（標題、副標、Section grouping、Grand Total、page footer）+ 自寫 CSV（無新增 CsvHelper 依賴），單一進入點 `ExportAsync(payload, format, path)`。QuestPDF Community License 由 `Interlocked.Exchange` 一次性設定。
- **WPF Reports 頁** — `ReportsViewModel` 注入四個 service，`LoadAsync` 同時載入三大報表並暴露 `IncomeStatement` / `BalanceSheet` / `CashFlowStatement` 觀察屬性；`ReportsView.xaml` 三個 Expander 顯示資料 + 6 個 Export 命令（PDF/CSV ×3）走 `SaveFileDialog`。

### 內部變更

- `Assetra.Application/Reports/Statements/`、`Assetra.Application/Reports/ReportExportService.cs`。
- `Assetra.WPF/Infrastructure/ReportsServiceCollectionExtensions.cs` 註冊四個介面為 Singleton。
- `Languages/zh-TW.xaml` / `en-US.xaml` 新增 `Reports.IncomeStatement.Title` / `Reports.BalanceSheet.Title` / `Reports.CashFlow.Title` / `Reports.Export.Pdf` / `Reports.Export.Csv`。
- 444 → 455 筆測試全綠（新增 4 筆 `ReportPeriod` + 3 筆 `IncomeStatementService` + 2 筆 `BalanceSheetService` + 2 筆 `CashFlowStatementService`）。

## v0.10.0 - 2026-04-28

Reconciliation Phase 2：補上 v0.9.0 暫緩的「新建 Session UI／Created／OverwrittenFromStatement 執行路徑／餘額對帳面板／Kind 分組」。

### 重點

- **D1 共用 IImportRowApplier** — 從 `ImportApplyService` 抽出 `IImportRowApplier`（Core 介面）與 `DefaultImportRowApplier`（Application 實作），讓 Reconciliation 在「Created」處置時不必另寫 trade-from-row 邏輯，直接共用 ImportRowMapper + AutoCategorizationRule snapshot。
- **D1-2 期末餘額欄位** — `ReconciliationSession` 新增 `StatementEndingBalance` 欄；SQLite 透過 `SqliteSchemaHelper.MigrateAddColumn` 加 `statement_ending_balance REAL`，向下相容既有 session 列為 NULL。
- **F1 新建 Session 面板** — `ReconciliationView.xaml` 新增可摺疊新建面板（Account 下拉、起訖期間、來源切換 = 既有匯入批次 vs 上傳新檔、期末餘額），ViewModel 端整合 `IImportBatchHistoryRepository` / `IImportFormatDetector` / `ImportParserFactory` 兩種來源路徑。
- **F2 Created / OverwrittenFromStatement** — `IReconciliationService.ApplyResolutionAsync` 新增 `(sourceKind, options)` overload；Created → 透過 IImportRowApplier 把 statement row 寫入為 trade，OverwrittenFromStatement → `_trades.GetByIdAsync` + `with { CashAmount = srow.Amount }` 後 Update。動作按鈕視 Kind 動態顯示。
- **F3 Kind 分組 + 餘額面板** — DataGrid 改綁 `GroupedDiffs` ICollectionView（PropertyGroupDescription on KindDisplay）；右側面板顯示 `Statement Sum / Trades Sum / Δ / Ending balance` 簡化餘額對帳。

### 內部變更

- `Assetra.Core/Interfaces/Import/IImportRowApplier.cs`、`Assetra.Application/Import/DefaultImportRowApplier.cs`、`Assetra.WPF/Infrastructure/ImportServiceCollectionExtensions.cs` 註冊。
- `ReconciliationServiceCollectionExtensions.cs` 注入 `ITradeRepository` / `IReconciliationMatcher` / `IImportBatchHistoryRepository` / `IImportFormatDetector` / `ImportParserFactory` 至 ViewModel。
- `ReconciliationDiffRowViewModel` 暴露 `IsMissing` / `IsExtra` / `IsAmountMismatch` 供 XAML 動作按鈕 visibility binding。
- `Languages/*.xaml` 新增 14 組 `Reconciliation.NewSession.*` / `Reconciliation.Action.CreateTrade` / `Reconciliation.Action.Overwrite` / `Reconciliation.Balance.Title` 鍵。
- 444 → 444 筆測試全綠（既有測試相容；本 sprint 主為 UI/連線改動，暫未新增 unit test）。

## v0.9.0 - 2026-04-28

新增 Reconciliation bounded context：把對帳單預覽列與已匯入的 Trade 比對，找出 Missing / Extra / AmountMismatch 三類差異，並提供逐筆裁決與簽核流程。

### 重點

- **Reconciliation 領域模型（F1）** — 新增 `ReconciliationSession`（帳戶 + 期間 + 來源批次 + 狀態）與 `ReconciliationDiff`（Kind = Missing / Extra / AmountMismatch；Resolution = Pending / Created / Deleted / MarkedResolved / Ignored / OverwrittenFromStatement），並建立 `IReconciliationSessionRepository` + `ReconciliationSessionSqliteRepository`，以 `statement_rows_json` 將整批對帳單列存於 session row、diff 內 `statement_row_json` 保留個別列 snapshot 供後續比對與顯示。
- **Diff 比對演算法（F2）** — `ReconciliationService.ComputeDiffs` 以 `IReconciliationMatcher`（預設 `DefaultReconciliationMatcher`：日期 ±1 天、金額容忍 0.005，sign-aware）雙向配對對帳單列與 trade；金額完全相等 → 不產 diff，差異在容忍度內但不相等 → AmountMismatch。`EnsureLegalTransition` 強制 Kind × Resolution 合法表，杜絕 UI 端錯置裁決。
- **Reconciliation Tab（F3）** — Import 頁改為 TabControl，新增 Reconciliation 分頁；toolbar 內含 Session 下拉、Recompute、SignOff，DataGrid 顯示 Kind / Date / Amount / Counterparty / Resolution / Actions（MVP 動作集：Mark Resolved / Ignore / Delete Trade），底部顯示 Pending / Resolved / Total 計數。
- **D1 — 跨 context 共用準備** — `ImportPreviewRow` 標註可被 Reconciliation 沿用；`ImportBatchEntry` 新增 `PreviewRowJson` 並由 `ImportApplyService` 寫入，使 `IImportBatchHistoryRepository.GetPreviewRowsAsync` 能成為對帳單來源。

### 內部變更

- 新增 `Assetra.Core/Models/Reconciliation/`、`Assetra.Core/Interfaces/Reconciliation/`、`Assetra.Core/DomainServices/Reconciliation/`、`Assetra.Application/Reconciliation/`、`Assetra.Infrastructure/Persistence/Reconciliation*`、`Assetra.WPF/Features/Reconciliation/`。
- `AppBootstrapper` 加入 `AddReconciliationContext`；`SqliteSchemaHelper.KnownTables` 補上 `import_batch_history` / `import_batch_entry` / `reconciliation_session` / `reconciliation_diff`；`ImportBatchHistorySchemaMigrator` 以 `MigrateAddColumn` 新增 `preview_row_json` 欄。
- `ReconciliationService` 暴露 `public static ComputeDiffs(...,matcher)` 與 `public static EnsureLegalTransition(...)` 供 unit test 直接驅動，避免測試需要實例化 service。
- `Languages/zh-TW.xaml` / `en-US.xaml` 新增 `Import.Tab.Import` 與 14 組 `Reconciliation.*` 鍵（Tab / Title / Subtitle / Session / Recompute / SignOff / 6 個 Col.*、3 個 Action.*）。
- 421 → 444 筆測試全綠（新增 7 筆 `DefaultReconciliationMatcher` + 16 筆 `ReconciliationService`，含 Kind × Resolution 合法表 Theory）。

### 暫不納入（後續 sprint）

- `Created` / `OverwrittenFromStatement` 兩種 Resolution 在 UI 上的執行路徑（需先打通 `ImportRowMapper` 與 trade 寫入，目前服務端可接受、UI 暫未提供按鈕）。
- 「新建 Reconciliation Session」對話框（目前由程式碼路徑 `CreateAsync` 建立；UI 只能載入既有 session）。
- 即時餘額對帳面板與 DataGrid Kind 分組。

## v0.8.0 - 2026-04-27

收尾 Import Governance Phase 2：把匯入端與手動端的自動分類規則整合為單一規則系統，加入批次歷史與 rollback。

### 重點

- **統一自動分類規則（F1）** — 將匯入用 `ImportRule` 與手動用 `AutoCategorizationRule` 整合為同一個模型；新增 `Name`、`MatchField`（對方／備註／兩者任一／完整內文）、`MatchType`（包含／等於／開頭是／正規表達式）與 `[Flags] AppliesTo`（Manual / Import / Both）。既有規則以「AnyText + Contains + Both」做向後相容預設值。
- **匯入時自動帶入分類（F2）** — `ImportRowMapper` / `ImportApplyService` 套用規則 snapshot；命中規則時自動帶入 `Trade.CategoryId`，未命中保持空。
- **批次歷史 + Rollback（F3）** — `ImportBatchHistoryRepository` 紀錄每批匯入的 entries（新增 / 覆蓋 / 跳過 + JSON snapshot），`ImportRollbackService` 可一鍵還原已套用的批次；UI 新增 Import 歷史摺疊區與 rollback 按鈕。
- **Categories 進階規則 UX（C4）** — 規則行 inline 編輯下方新增「進階選項」Expander，包含 MatchField / MatchType 單選、AppliesTo 雙勾選與「即時測試」面板（輸入對方／備註範例即顯示 ✓／✗），新增規則表單同步擁有相同進階區。預設摺疊；簡單模式視覺零變化。

### 內部變更

- 擴充 `AutoCategorizationRule` 為 record-with 模式並向後相容（新欄位皆有預設值）；schema 以 `ALTER TABLE` 加上 `name` / `match_field` / `match_type` / `applies_to` 欄位。
- `AutoCategorizationEngine` 改為 dual-API：保留 `Match(string?, rules)` 給手動路徑，新增 `Match(AutoCategorizationContext, rules)` 並依 `AppliesTo & Source` 過濾。
- `ImportApplyService` 接收選用 `IAutoCategorizationRuleRepository`；DI 端在 `ImportServiceCollectionExtensions` 用 lambda factory 串接。
- `Languages/zh-TW.xaml` / `en-US.xaml` 新增 19 組 `Categories.Rule.*` 鍵（Advanced / MatchField / MatchType / AppliesTo / LiveTest 等）。
- 421 筆測試全綠（含 7 筆新引擎測試 + 2 筆 ImportApplyService 自動分類測試）。

## v0.7.0 - 2026-04-27

新增 Import Governance：把銀行對帳單與券商交易明細的 CSV / Excel 匯入到 Assetra，並自動偵測重複交易。

### 重點

- **匯入功能（v0.7 主題）** — 新增 `Import` bounded context，支援 Top 5 銀行（國泰世華 / 玉山 / 中信 / 台新 / 富邦）與 Top 5 券商（元大 / 富邦 / 凱基 / 永豐金 / 群益）對帳單。CSV 與 Excel（.xlsx / .xls）皆可，UTF-8 / Big5 編碼自動辨識。
- **格式驅動的 Parser** — 解析行為由 `CsvParserConfigs` / `ExcelParserConfigs` 宣告式定義；新增或修正某家銀行 / 券商格式時只要改 config 不必動程式碼。
- **重複交易偵測** — 以 `date | abs(amount) | symbol` 為跨資料庫比對 key，UI 預覽列以 Skip / Overwrite / Add anyway 三種處置方式呈現。
- **Modern UX 匯入頁** — 拖放區、自動偵測格式 chip、預覽 DataGrid（含每列衝突處理下拉）、現金帳戶選擇（必選才能套用）、結果 snackbar。

### 內部變更

- 新增 `Assetra.Core/Models/Import/`、`Assetra.Core/Interfaces/Import/`、`Assetra.Application/Import/`、`Assetra.Infrastructure/Import/`、`Assetra.WPF/Features/Import/`。
- 新增套件 `CsvHelper` 33.0.1、`ClosedXML` 0.105.0。
- `AppBootstrapper` 加入 `AddImportContext()`；NavRail 在 Settings 上方加入 Import 入口（Segoe Fluent `&#xE8B5;`）。
- 392 → 多筆 import 測試（Core models、parsers、format detector、conflict detector、apply service）全綠。

## v0.6.0 - 2026-04-26

收尾 v0.6.0 sprint：月結報告 UI、淨資產趨勢視覺化，以及 Goals MVP。

### 重點

- **月結報告 UI（F1）** — 新增 Reports 頁面，含月份選擇器、四張指標卡（收入 / 支出 / 淨額 / 儲蓄率，皆附與上月差額）、超支清單、近期到期清單，後端由 `MonthEndReportService` 提供。
- **淨資產趨勢（F2）** — 新增 Trends 頁面，沿用 `PortfolioHistoryViewModel`。提供 30 / 90 / 180 / 365 / All 預設區間按鈕，加上 `DateRangePicker` 自訂範圍（兩端皆設定時覆蓋預設）。
- **Goals MVP（F3）** — 全新 bounded context：`FinancialGoal` 模型、`IFinancialGoalRepository` + `GoalSqliteRepository`、`GoalSchemaMigrator`，並新增 Goals 頁面含進度條、期限顯示與新增表單（FormTextBox + FormDatePicker + 欄位標籤）。
- **SetupNotice 切片** — 從 `PortfolioViewModel` 抽出 `SetupNoticeViewModel`（顯示 / 標題 / 訊息 / 動作文字 + 執行命令），延續 v0.6.0 的 D1-2 SubViewModels 重構工作。
- **主題 / 語言 / 字級稽核** — 三個新頁面：`AppText` → `AppTextPrimary`、`FontSize="{StaticResource …}"` → `{DynamicResource …}`，確保執行期主題與字級切換能即時生效。

### 內部變更

- 新增 `Assetra.WPF/Features/Reports/`、`Assetra.WPF/Features/Trends/`、`Assetra.WPF/Features/Goals/` 功能資料夾，並在 Nav rail 與 `MainViewModel` / `MainWindow` 接好內容區塊。
- 在 `AppBootstrapper` 註冊 `AddGoalsContext` DI 擴充方法。
- `zh-TW.xaml` / `en-US.xaml` 新增 `Trends.CustomRange`、`Goals.Add.Deadline` 等語言鍵。

## v0.5.8 - 2026-04-26

本版優化在地化提示文字並整理專案文件。

### 重點

- 調整 zh-TW 與 en-US 的「現金 / 信用卡 / 負債」新增對話框提示文字，反映目前「建立時即記錄初始條目」的流程，取代舊的「之後再去新增記錄」說法。
- 補上 `CHANGELOG.md` 中遺漏的 v0.4.1、v0.5.6、v0.5.7 條目。
- 移除 `docs/INDEX.md` 中失效的 `Downloads` 連結。

## v0.5.7 - 2026-04-26

本版優化投資組合對話框與表格的響應式體驗。

### 重點

- 新增紀錄對話框中的切換配對強制以橫向兩欄排列（窄寬時不再降回直向）。
- 透過覆寫預設的 `ListBoxItem` 範本，修正投資 Position 卡片 hover 矩形溢出。
- 將固定寬度的 `WrapPanel` 換成 `UniformGrid`，讓 position 統計儲存格能填滿可用寬度，無尾端空隙。
- Accounts / Liability DataGrid 第一欄欄頭由「資產」改為「名稱」；投資欄頭改為「標的」。
- 將 Accounts / Liability 儲存格範本重構為單列 `Grid`，使預設徽章能對齊整個儲存格高度的垂直中線。
- 新增紀錄底部按鈕順序改為符合 Windows 對話框慣例（取消在左，確認在右），並把「取消編輯」縮短為「取消」。

## v0.5.6 - 2026-04-25

本版修正負債建立流程的回歸問題。

### 重點

- 修正在某些狀態下，負債建立對話框未顯示貸款區塊的問題。

## v0.5.5 - 2026-04-24

本版優化啟動穩定性與復原行為。

### 重點

- 啟動時不再每次都因更新檢查而被阻塞。
- 僅在前次啟動未完成時才執行復原型啟動更新檢查。
- 維持日常啟動速度，同時保留損壞安裝的修復路徑。

### 內部變更

- 在 `App.xaml.cs` 新增 `startup.pending` 標記流程。
- 一般啟動流程改為主視窗顯示後在背景檢查更新。
- 復原更新路徑只在前次啟動疑似失敗時觸發。

## v0.5.4 - 2026-04-24

本版改善 Windows 應用程式品牌資產的細緻度。

### 重點

- 將模糊的 Windows 應用程式圖示路徑替換為專屬的多尺寸 Windows `.ico`。
- 應用程式 / 視窗圖示改為 Windows 專用資產，不再沿用網頁 favicon。

### 內部變更

- 新增 `Assets/windows/assetra-app.ico`。
- 更新 WPF 專案的圖示串接與主視窗圖示資源。

## v0.5.3 - 2026-04-24

本版在發生損壞安裝事件後，導入「啟動優先」的更新安全網。

### 重點

- 啟動時於主視窗開啟前先檢查更新。
- 啟動失敗時嘗試自我修復更新。

### 內部變更

- 此版本為過渡性安全版本，後續在 `v0.5.5` 進一步精修。

## v0.5.2 - 2026-04-24

本版修正啟動崩潰並強化幣別資料載入。

### 重點

- 修正初始 UI 載入時因徽章樣式造成的崩潰。
- 強化 Frankfurter 匯率解析以容忍缺失的 JSON 欄位。

### 內部變更

- `PortfolioBadgeBase` 不再透過脆弱的啟動路徑解析 `CornerRadius`。
- `CurrencyService` 採用容忍式 JSON 解析與安全 fallback。

## v0.5.1 - 2026-04-24

本版修正啟動畫面 / 圖示啟動可靠性問題。

### 重點

- 修正因啟動畫面圖示資源載入造成的啟動失敗。
- 改善打包安裝版本的啟動穩定性。

### 內部變更

- 啟動畫面圖示載入由靜態 XAML 資源解析改為以程式碼處理。
- 收緊圖示資產的資源打包規則。

## v0.5.0 - 2026-04-24

本版加入信用卡流程，並進行大規模響應式 UI 整修。

### 重點

- 新增信用卡資產與交易流程。
- 重組品牌資產與套件 logo 流程。
- 修飾 portfolio、alerts、settings、shell 與對話框版面的響應式表現。

### 內部變更

- 新增信用卡 workflow、schema 支援與回歸測試。
- 重做許多 WPF 版面，以更好支援較大的 `UiScale` 與較窄的寬度。

## v0.4.1 - 2026-04-23

本版新增 Fugle API key 設定的應用內指引。

### 重點

- 新增可從 Settings 頁面開啟的 Fugle 說明對話框。
- 在 zh-TW 與 en-US 提供設定步驟，使用者不必離開 app 即可完成設定。

## v0.4.0 - 2026-04-23

本版專注於更安全的 portfolio 編輯與可設定的市場資料來源。

### 重點

- 新增 `Fugle` 作為可設定的即時報價與歷史價格來源。
- 新增 `Settings` 欄位，分別管理報價來源、歷史來源與本機 Fugle API key 儲存。
- 新增於 Git 之外安全設定 Fugle API key 的文件。
- 重做記錄編輯流程：
  - 安全編輯模式
  - 建立修訂版
  - 取代原紀錄 / 兩者保留
- 將通用歡迎橫幅替換為任務導向的設定提示。

### 內部變更

- `StockScheduler` 可使用 Fugle，並在失敗時 fallback 至 TWSE/TPEX 官方來源。
- `DynamicHistoryProvider` 支援 `fugle`，與 `twse`、`yahoo`、`finmind` 並列。
- `PortfolioViewModel` 與測試保留向後相容的建構路徑，同時改用較新的 application 層服務。

## v0.3.0 - 2026-04-22

本版是 Assetra 第一個聚焦於架構的里程碑。

### 重點

- 加入更清晰的 `Core -> Application -> Infrastructure -> WPF` 結構。
- 將大多數 `Portfolio` 變更流程移入 application workflow services。
- 將 summary / load / history / query 職責移入專屬的 application services。
- 模組化啟動、schema migration、repository 初始化的責任。
- 在 `Portfolio` UI 部分引入較薄的 WPF 端 controllers / sub-viewmodels。
- 將 `Alerts` 重新置於 application 層介面（`IAlertService`）之後。
- 新增 workflow 層級測試與架構文件。

### 內部變更

- `PortfolioViewModel` 將更多行為委派給：
  - workflow services
  - query services
  - WPF 端 controllers / sub-viewmodels
- `FinancialOverviewViewModel` 改為透過 application query service 讀取資料。
- `.superpowers/` 本機工具產物已加入忽略，且不再追蹤。

## v0.2.0

application 層與架構整理工作之前較早期的產品里程碑。
