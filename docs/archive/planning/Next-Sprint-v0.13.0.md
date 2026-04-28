# v0.13.0 Sprint Plan — 風險分析

> 範圍：2 週。在 v0.12.0「投資績效分析」之上，擴充 Analysis Context 加入波動度 / 最大回撤 / Sharpe / 集中度，並串到 Alerts。
> 完成後 Reports「Performance」Expander 旁多一個「Risk」Expander，與績效並列。

## 一、版本目標

| # | 項目 | Bounded Context | 預估 |
|---|------|---|---|
| D1 | `RiskMetrics` / `DrawdownPoint` / `ConcentrationBucket` DTO | Analysis | S |
| F1 | `VolatilityCalculator` —— 日報酬 std dev × √252 | Analysis | S |
| F2 | `DrawdownCalculator` —— Max drawdown + drawdown 時序 | Analysis | M |
| F3 | `SharpeRatioCalculator` —— `(TWR − rf) / vol`，rf 暫以常數 0.02（descope: `IAppSettingsService` 推遲到 v0.14） | Analysis | S |
| F4 | `ConcentrationAnalyzer` —— Top-N 持倉佔比 + HHI | Analysis | M |
| ~~F5~~ | ~~`ConcentrationAlertRule`~~ **descoped** —— 改用 `RiskMetrics.HasConcentrationWarning` 計算屬性，避免擴張既有 price-target AlertRule 框架 | — | — |
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
  - 純計算 service：`Compute(annualizedReturn, annualizedVolatility, riskFreeRate)`
  - `Sharpe = (return − rf) / σ`；σ = 0 或 null → null
  - rf 由呼叫端傳入；目前 `ReportsViewModel.LoadRiskAsync` hardcode `0.02m`，待 v0.14（外幣/設定擴充）改走 `IAppSettingsService`

- **F4 ConcentrationAnalyzer**
  - 依賴 `IPortfolioRepository.GetEntriesAsync()` 取目前所有 PortfolioEntry
  - 加總 MarketValue → 算每筆權重
  - 輸出 Top 5 + 「Others」一筆
  - HHI = Σ w_i² ；單一持倉 100% → 1.0

- ~~**F5 ConcentrationAlertRule**~~ **descoped**
  - 原計畫於 `IAlertRuleService` 加規則；實際既有 `AlertRule` 為 price-target 專用（`AlertCondition.Above/Below`），擴張會牽動 schema
  - 改在 `RiskMetrics` 加 `HasConcentrationWarning` 計算屬性（>30% 單一部位 或 HHI >0.30），UI 直接綁
  - 若未來真的要走通用 Alert：建議重構 AlertRule 為 polymorphic 規則框架後再做

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
