# v0.11.0 Sprint Plan — Reports MVP

> 範圍：2–3 週。在 v0.10.0「Reconciliation Phase 2」之上，把已對帳的 trade 資料做成正式財報並可匯出 PDF / CSV。
> 完成後，Import → Reconciliation → Reports 形成「個人理財日記」對外輸出閉環。

## 一、版本目標

| # | 項目 | Bounded Context | 預估 |
|---|------|---|---|
| D1 | `ReportPeriod` / `ReportSection` / `StatementRow` / `ExportFormat` 共用 DTO | Reporting（新） | S |
| F1 | `IncomeStatementService` —— 損益表（收入 / 支出 / 淨利） | Reporting | M |
| F2 | `BalanceSheetService` —— 資產負債表（資產 / 負債 / 淨值） | Reporting | M |
| F3 | `CashFlowStatementService` —— 現金流量表（營業 / 投資 / 融資三段） | Reporting | M |
| F4 | `ReportExportService` —— PDF（QuestPDF）+ CSV（CsvHelper），Reports 頁加「匯出」按鈕 | Reporting / WPF | M |

## 二、缺口全景

### P0（本 sprint 範圍）

- D1：`Assetra.Core/Models/Reports/`（共用 DTO）+ `Assetra.Application/Reports/Statements/` 子目錄（三張表 service）
- F1：`IncomeStatementService.GenerateAsync(ReportPeriod period)`
  - 收入：`Trade.Type == Income` 累加，依 `ExpenseCategory` 分組
  - 支出：`Trade.Type == Expense` 累加，依 `ExpenseCategory` 分組
  - 淨利 = 收入 − 支出；同時帶上一期數字做對比
  - 輸出 `IncomeStatement(Period, IncomeRows[], ExpenseRows[], Net, PriorPeriod)`
- F2：`BalanceSheetService.GenerateAsync(DateOnly asOf)`
  - 資產：用 `IPortfolioHistoryQueryService` 取 `asOf` 當天的 `PortfolioDailySnapshot` —— 現金 / 投資 / 其他資產
  - 負債：`AssetItem` 中 `FinancialType.Liability` 餘額累加（含信用卡）
  - 淨值 = 資產 − 負債
  - 輸出 `BalanceSheet(AsOf, AssetRows[], LiabilityRows[], NetWorth)`
- F3：`CashFlowStatementService.GenerateAsync(ReportPeriod period)`
  - 營業：日常收入 / 支出（沿用 IncomeStatement 結果）
  - 投資：`Trade.Type ∈ {Buy, Sell, Dividend}` cash flows
  - 融資：負債變動（信用卡刷卡、還款；貸款撥款、還款）
  - 三段加總 = 期間現金淨變動，驗證：期初現金 + 淨變動 = 期末現金
- F4：`ReportExportService`
  - PDF：QuestPDF 0.x（MIT）—— 三張表共用 `StatementDocument` template，含標題 / 期間 / 表體（DataGrid 風格）/ 總計列 / 頁尾
  - CSV：CsvHelper（既有依賴）—— 每張表一個 CSV，header / rows / total
  - Reports 頁三個區塊（既有月結 + 三新表）右上角各加「匯出 PDF」「匯出 CSV」按鈕，呼叫 `Microsoft.Win32.SaveFileDialog`

### P1（下一輪）

- 季 / 年期間切換（目前 MonthEndReportService 是月，新三表先共用同一 period selector）
- 比較期切換（QoQ / YoY）
- 圖表嵌入 PDF（pie / bar）

### P2（範圍邊緣）

- 多帳戶 / 多幣別合併（屬 v0.12+ 外幣 sprint）
- Excel 匯出（屬 P1）
- 報表訂閱排程（屬 Recurring 擴充）

## 三、動工前要先處理的技術債

### D1-1 共用 DTO

```csharp
public sealed record ReportPeriod(DateOnly Start, DateOnly End)
{
    public static ReportPeriod Month(int year, int month);
    public static ReportPeriod Year(int year);
    public ReportPeriod Prior();          // 上一期（同 length）
}

public sealed record StatementRow(string Label, decimal Amount, string? Group);
public sealed record StatementSection(string Title, IReadOnlyList<StatementRow> Rows, decimal Total);
public enum ExportFormat { Pdf, Csv }
```

### D1-2 套件加裝

- **QuestPDF 2024.x**（MIT；商用免費）—— PDF 生成
- 既有 `CsvHelper` —— CSV 匯出，無需加裝

### D1-3 把 `MonthEndReportService` 抽 `ReportPeriod` 參數

目前 `MonthEndReportService.GenerateAsync(int year, int month)`，改為 overload 接 `ReportPeriod`，內部相容；新三表一律走 `ReportPeriod` 介面。

## 四、F1 / F2 / F3 / F4 設計要點

### F1 IncomeStatementService

```csharp
public sealed record IncomeStatement(
    ReportPeriod Period,
    StatementSection Income,
    StatementSection Expense,
    decimal Net,
    IncomeStatement? Prior);

public interface IIncomeStatementService
{
    Task<IncomeStatement> GenerateAsync(ReportPeriod period, CancellationToken ct = default);
}
```

實作走 `ITradeRepository.GetAllAsync` → filter by date / type → group by `CategoryId` → `LookupAsync` 取 ExpenseCategory.Name 為 Group label。

### F2 BalanceSheetService

```csharp
public sealed record BalanceSheet(
    DateOnly AsOf,
    StatementSection Assets,
    StatementSection Liabilities,
    decimal NetWorth);
```

資產來源：
- 現金：`IAssetRepository.GetItemsByTypeAsync(FinancialType.Asset)` 取 cash 子類別 + `Trade` 累加至 `asOf`
- 投資：`PortfolioDailySnapshot.AsOf == asOf` 取持倉市值
- 其他：信用卡額度 / 押金 等

負債來源：
- `FinancialType.Liability` items + 對應 `Trade` 累加至 `asOf`

### F3 CashFlowStatementService

```csharp
public sealed record CashFlowStatement(
    ReportPeriod Period,
    StatementSection Operating,
    StatementSection Investing,
    StatementSection Financing,
    decimal NetChange,
    decimal OpeningCash,
    decimal ClosingCash);
```

驗證 `OpeningCash + NetChange == ClosingCash`，否則 throw（提示對帳未完成）。

### F4 ReportExportService

```csharp
public interface IReportExportService
{
    Task ExportAsync(IncomeStatement statement, ExportFormat format, string filePath, CancellationToken ct = default);
    Task ExportAsync(BalanceSheet statement, ExportFormat format, string filePath, CancellationToken ct = default);
    Task ExportAsync(CashFlowStatement statement, ExportFormat format, string filePath, CancellationToken ct = default);
}
```

PDF template `StatementDocument`（QuestPDF）統一表頭 / 表尾 / 字型 / 列高；三張表只差表體。

#### Reports 頁 UI 變更

- 既有月結報告區塊保留
- 新增三個 Expander：「損益表」「資產負債表」「現金流量表」，預設摺疊
- 共用頂端 `ReportPeriod` selector（月 / 季 / 年；asOf 由 PeriodEnd 推）
- 每個 Expander 右上角：`Export PDF` / `Export CSV` 兩個 IconButton

#### i18n keys (~24 組)

`Reports.IncomeStatement.{Title,Income,Expense,Net,Prior}`、`Reports.BalanceSheet.{Title,Assets,Liabilities,NetWorth,AsOf}`、`Reports.CashFlow.{Title,Operating,Investing,Financing,NetChange,Opening,Closing}`、`Reports.Export.{Pdf,Csv,SaveDialog,Success,Failed}`

## 五、測試重點

| 層 | 重點 |
|---|---|
| Application | `IncomeStatementService` 三類 trade 分組正確、空期間 / 無資料 / Prior == null |
| Application | `BalanceSheetService` `AsOf` 取最近 snapshot、無 snapshot fallback、負債計算 |
| Application | `CashFlowStatementService` 三段加總 = NetChange、OpeningCash + NetChange == ClosingCash 驗證、不平衡時拋例外 |
| Application | `ReportExportService` PDF 產出非空（檔案大小 > 0、PDF magic bytes）、CSV header + rows count 正確 |
| Tests count | 預估 +30–40 筆，總數應達 ~492–500 |

## 六、文件 / 收尾

- CHANGELOG v0.11.0 條目（D1 / F1 / F2 / F3 / F4）
- `docs/architecture/Bounded-Contexts.md` Reporting Context 從「部分實作」改「MVP 完成」，列出三張表 service
- `docs/planning/Implementation-Roadmap.md` Phase 2 §2「報表系統」全部 checkbox 打勾
- README 新增「Reports」章節示意截圖
- 標 v0.11.0 tag

## 七、風險與取捨

- **CashFlow 三段定義**：個人財務沒有 GAAP 強制定義，自訂三段「日常 / 投資 / 借貸」即可；不要陷入會計準則細節，要做「可讀」而非「可審計」
- **PDF 字型**：QuestPDF 預設字型不含中文，需嵌入 NotoSansTC（OFL，已在 Languages 一致）；首次匯出要 lazy-load 字型，避免拖慢 app startup
- **BalanceSheet 不依賴 PortfolioDailySnapshot**：若使用者沒每日快照，fallback 為「以 trade 累加 + 最近一次 quote」；快照當作優化路徑
- **匯出失敗回饋**：用 snackbar 顯示「已匯出至 {path}」，失敗時顯示具體例外，不要靜默
