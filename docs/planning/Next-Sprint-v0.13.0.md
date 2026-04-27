# v0.13.0 Sprint Plan — 風險分析

> 範圍：2 週。在 v0.12.0「投資績效分析」之上，擴充 Analysis Context 加入波動度 / 最大回撤 / Sharpe / 集中度，並串到 Alerts。
> 完成後 Reports「Performance」Expander 旁多一個「Risk」Expander，與績效並列。

## 一、版本目標

| # | 項目 | Bounded Context | 預估 |
|---|------|---|---|
| D1 | `RiskMetrics` / `DrawdownPoint` / `ConcentrationBucket` DTO | Analysis | S |
| F1 | `VolatilityCalculator` —— 日報酬 std dev × √252 | Analysis | S |
| F2 | `DrawdownCalculator` —— Max drawdown + drawdown 時序 | Analysis | M |
| F3 | `SharpeRatioCalculator` —— `(TWR − rf) / vol`，rf 從 `IAppSettingsService` | Analysis | S |
| F4 | `ConcentrationAnalyzer` —— Top-N 持倉佔比 + HHI | Analysis | M |
| F5 | `ConcentrationAlertRule` —— 超閾值時生 Alert | Analysis / Alerts | S |
| F6 | Reports「Risk」Expander + DI | WPF / Reporting | M |

## 二、缺口全景

### P0（本 sprint 範圍）

- **D1 共用 DTO**（`Assetra.Core/Models/Analysis/`）
  - `RiskMetrics(decimal? Volatility, decimal? MaxDrawdown, decimal? SharpeRatio, IReadOnlyList<ConcentrationBucket> TopHoldings, decimal? Hhi)`
  - `DrawdownPoint(DateOnly Date, decimal Value, decimal Peak, decimal Drawdown)`
  - `ConcentrationBucket(string Label, decimal Weight)`

- **F1 VolatilityCalculator**
  - input: `IReadOnlyList<(DateOnly Date, decimal Value)>` — 日線市值序列
  - 計算日報酬率 `r_i = (V_i / V_(i-1)) − 1`（過濾零除）
  - 樣本標準差 σ × √252 = 年化波動度
  - <2 筆觀察值 → null

- **F2 DrawdownCalculator**
  - 同樣輸入時序值
  - 維護 running peak，每點 drawdown = (peak − current) / peak
  - 輸出 `IReadOnlyList<DrawdownPoint>` + `decimal MaxDrawdown`
  - public static `Compute(values)` + `MaxDrawdown(values)` 便利方法

- **F3 SharpeRatioCalculator**
  - 依賴 `ITimeWeightedReturnCalculator`、`IVolatilityCalculator`、`IAppSettingsService`（取 risk-free rate, 預設 0.02）
  - `Sharpe = (TWR − rf) / σ`；σ = 0 或 null → null

- **F4 ConcentrationAnalyzer**
  - 依賴 `IPortfolioRepository.GetEntriesAsync()` 取目前所有 PortfolioEntry
  - 加總 MarketValue → 算每筆權重
  - 輸出 Top 5 + 「Others」一筆
  - HHI = Σ w_i² ；單一持倉 100% → 1.0

- **F5 ConcentrationAlertRule**
  - 既有 `IAlertRuleService` 加一條規則：若任一持倉權重 > 30% 或 HHI > 0.30，產 Alert
  - 與 v0.x 既有 alert 框架共用 schema

- **F6 Reports Risk Expander**
  - `ReportsView.xaml` 新增第 5 個 Expander，標題 `Reports.Risk.Title`
  - 顯示 Volatility / Max Drawdown / Sharpe + Top Holdings list
  - `ReportsViewModel` 注入新 service，`LoadAsync` 拉取

### P1（下一輪）

- Sortino Ratio（只看下行波動）
- Beta vs benchmark
- 風險拆解圖表（pie / treemap）

### P2（範圍邊緣）

- 信心區間 / VaR（屬量化分析模組）
- 因子分析（Fama-French）

## 三、檔案地圖

```
Assetra.Core/
├── Models/Analysis/
│   ├── RiskMetrics.cs
│   ├── DrawdownPoint.cs
│   └── ConcentrationBucket.cs
└── Interfaces/Analysis/
    ├── IVolatilityCalculator.cs
    ├── IDrawdownCalculator.cs
    ├── ISharpeRatioCalculator.cs
    └── IConcentrationAnalyzer.cs

Assetra.Application/Analysis/
├── VolatilityCalculator.cs
├── DrawdownCalculator.cs
├── SharpeRatioCalculator.cs
└── ConcentrationAnalyzer.cs

Assetra.WPF/
├── Features/Reports/ReportsView.xaml          (+1 Expander)
├── Features/Reports/ReportsViewModel.cs       (+4 deps + Risk prop)
└── Infrastructure/AnalysisServiceCollectionExtensions.cs (+4 reg)

Assetra.Tests/Application/Analysis/
├── VolatilityCalculatorTests.cs               (~3)
├── DrawdownCalculatorTests.cs                 (~3)
├── SharpeRatioCalculatorTests.cs              (~2)
└── ConcentrationAnalyzerTests.cs              (~3)
```

## 四、測試策略

- Volatility：手算 `[100, 102, 99, 101]` 日報酬序列，驗證樣本 std × √252
- Drawdown：構造 `[100, 120, 90, 110, 80]` → peak=120, trough=80 → MDD = 33.33%
- Sharpe：給定 TWR=10%, vol=15%, rf=2% → (0.10-0.02)/0.15 ≈ 0.533
- Concentration：3 筆 50%/30%/20% → Top3 完整，HHI = 0.25+0.09+0.04 = 0.38

## 五、文件 / 收尾

- CHANGELOG v0.13.0 條目
- Bounded-Contexts.md：Analysis Context 條目補上四個新 service
- Implementation-Roadmap.md：勾選「風險分析」三項
- 標 v0.13.0 tag
