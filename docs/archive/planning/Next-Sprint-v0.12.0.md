# v0.12.0 Sprint Plan — 投資績效分析

> 範圍：2–3 週。在 v0.11.0「Reports MVP」之上，把 Trade journal 的買賣 / 股利 / 持倉資料做成正式的績效指標（XIRR / TWR / MWR），並提供 benchmark 對比與損益歸因。
> 完成後 Reports 頁多一張「Performance」報表，與三大財報並列。

## 一、版本目標

| # | 項目 | Bounded Context | 預估 |
|---|------|---|---|
| D1 | `CashFlow` / `PerformancePeriod` / `PerformanceResult` / `AttributionBucket` 共用 DTO | Analysis（新） | S |
| F1 | `XirrCalculator` —— Newton-Raphson + bisection fallback | Analysis | M |
| F2 | `TimeWeightedReturnCalculator` —— sub-period 幾何鏈接 | Analysis | M |
| F3 | `MoneyWeightedReturnCalculator` —— XIRR 包裝（單一持倉 / 整體） | Analysis | S |
| F4 | `BenchmarkComparisonService` —— 0050 / SPY 同期 TWR 對比 | Analysis | M |
| F5 | `PnlAttributionService` —— 已實現 / 未實現 / 股利 / 手續費 拆解 | Analysis | M |
| F6 | Reports「Performance」Tab + PDF/CSV 沿用 v0.11 export | WPF / Reporting | M |

## 二、缺口全景

### P0（本 sprint 範圍）

- **D1 共用 DTO**（`Assetra.Core/Models/Analysis/`）
  - `CashFlow(DateOnly Date, decimal Amount)` — Amount: 流出為負、流入為正
  - `PerformancePeriod(DateOnly Start, DateOnly End)` — 與 ReportPeriod 同形但語意專屬
  - `PerformanceResult(decimal Xirr, decimal Twr, decimal Mwr, decimal? BenchmarkTwr, IReadOnlyList<AttributionBucket> Attribution)`
  - `AttributionBucket(string Label, decimal Amount)`

- **F1 XirrCalculator**
  - 公式：解 `Σ amount_i / (1+r)^((d_i - d_0)/365) = 0`
  - Newton-Raphson 主迴圈，max 100 iter、tolerance 1e-7
  - 收斂失敗 fallback 至 bisection（[-0.99, 10.0]）
  - input 必須至少一筆正、一筆負流，否則 throw `InvalidOperationException`
  - public static `Compute(IReadOnlyList<CashFlow>, double guess = 0.1)`

- **F2 TimeWeightedReturnCalculator**
  - 在每筆外部 cash flow（Buy 加碼 / Sell 部分賣出）切 sub-period
  - 每段 R_i = (V_end − V_start − netFlow) / V_start
  - TWR = Π(1 + R_i) − 1
  - input：`IReadOnlyList<CashFlow> flows` + `IReadOnlyList<(DateOnly, decimal)> valuations`（持倉市值序列）

- **F3 MoneyWeightedReturnCalculator**
  - 對單一 `PortfolioEntry`：取所有相關 trade（Buy / Sell / CashDividend）做 cash flow，加期末市值為 terminal flow，呼 `XirrCalculator`
  - 對整體 portfolio：合併所有 entries 的 cash flow

- **F4 BenchmarkComparisonService**
  - 給定 period，拉 0050 / SPY 同期收盤價（透過既有 `IStockHistoryProvider`）
  - 計算「同期間若把所有外部投入金額按比例投入 benchmark」的 TWR
  - 輸出 `decimal BenchmarkTwr`

- **F5 PnlAttributionService**
  - 走 trade journal 對 period 做拆解：
    - 已實現損益（Sell 的 RealizedPnl 加總）
    - 未實現損益（期末持倉 MarketValue − 加權平均成本）
    - 股息（CashDividend 加總）
    - 手續費（Commission 累加，負值）
  - 輸出 `IReadOnlyList<AttributionBucket>`，套 v0.11 `StatementSection` 顯示

- **F6 Performance Tab**
  - `ReportsView.xaml` 第 4 個 Expander，標題 `Reports.Performance.Title`
  - 顯示 XIRR / TWR / MWR / Benchmark Δ + Attribution rows
  - 重用 `ReportExportService`（將 PerformanceResult 轉 `IncomeStatement`-like 結構）

### P1（下一輪）

- 多 benchmark 同時對比（0050 + 0056 + SPY）
- Sharpe / Sortino（需 risk-free rate config）
- 個別持倉 vs 大盤散點圖

### P2（範圍邊緣）

- Holding-period 收益分布圖（屬風險分析 sprint）
- 投組風險拆解（屬 v0.13+ 風險分析）

## 三、動工前要先處理的技術債

無——所有依賴 (`Trade`, `IPortfolioRepository`, `IStockHistoryProvider`) 皆已存在。新增 namespace `Assetra.Application/Analysis/` 維持依賴方向。

## 四、檔案地圖

```
Assetra.Core/
├── Models/Analysis/
│   ├── CashFlow.cs
│   ├── PerformancePeriod.cs
│   ├── PerformanceResult.cs
│   └── AttributionBucket.cs
└── Interfaces/Analysis/
    ├── IXirrCalculator.cs
    ├── ITimeWeightedReturnCalculator.cs
    ├── IMoneyWeightedReturnCalculator.cs
    ├── IBenchmarkComparisonService.cs
    └── IPnlAttributionService.cs

Assetra.Application/
└── Analysis/
    ├── XirrCalculator.cs
    ├── TimeWeightedReturnCalculator.cs
    ├── MoneyWeightedReturnCalculator.cs
    ├── BenchmarkComparisonService.cs
    └── PnlAttributionService.cs

Assetra.WPF/
├── Features/Reports/ReportsView.xaml         (+1 Expander)
├── Features/Reports/ReportsViewModel.cs       (+5 service deps + Performance prop)
└── Infrastructure/AnalysisServiceCollectionExtensions.cs

Assetra.Tests/
└── Application/Analysis/
    ├── XirrCalculatorTests.cs                 (~5 tests，含經典案例 Excel XIRR 對齊)
    ├── TimeWeightedReturnCalculatorTests.cs   (~3)
    ├── MoneyWeightedReturnCalculatorTests.cs  (~2)
    ├── BenchmarkComparisonServiceTests.cs     (~2，stub IStockHistoryProvider)
    └── PnlAttributionServiceTests.cs          (~3)
```

## 五、測試策略

- XIRR：用 Excel/LibreOffice XIRR 驗證的經典案例（如 `[-1000, 0/1, 100, 30/6, 200, 60/12, 800, 240/24]` → 約 11.65%）
- TWR：人工建構「外部投入後上漲 / 下跌」混合期間，驗證 TWR ≠ 簡單報酬率
- MWR：驗證整體 portfolio = 合併 entry cash flow 後的 XIRR
- Attribution：建構含 realized/unrealized/dividend/commission 的測試 portfolio，驗證四桶總和 = 期末淨損益

## 六、文件 / 收尾

- CHANGELOG v0.12.0 條目
- Bounded-Contexts.md：新增 8. Analysis Context
- Implementation-Roadmap.md：勾選「投資績效分析」四項
- 標 v0.12.0 tag
