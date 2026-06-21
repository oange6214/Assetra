# 資產趨勢 vs 大盤 對比重構 (Phase 0+1) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 讓「我的投組」對比線改用 TWR 序列（與 benchmark 同基準、消除建倉/賣股假尖峰），並把比較項目換成 Google 式可移除 chips ＋ 搜尋 popup（autocomplete）。

**Architecture:** Phase 0 在 `ITimeWeightedReturnCalculator` 加 `ComputeSeries`（每日累積 TWR），`PortfolioHistoryViewModel` 的 % 對比圖「我的投組」線改吃此序列。Phase 1 注入 `IStockSearchService` 做 autocomplete，`ComparisonLegendItem` 加 `RemoveSymbol` 讓 chips 可移除，`TrendsView.xaml` 換成 chips ＋「＋比較」popup。

**Tech Stack:** C# / .NET 10、WPF、CommunityToolkit.Mvvm、LiveCharts2、xUnit。

**Build/Test 慣例（繞開執行中 app 的 DLL 鎖 ＋ NU1903 audit）：**
- Build：`dotnet build Assetra.Tests/Assetra.Tests.csproj -o $env:TEMP\asmtests --no-restore -p:NuGetAudit=false`
- Test：`dotnet vstest $env:TEMP\asmtests\Assetra.Tests.dll --TestCaseFilter:"<filter>"`
- 全套（排除 WPF 控制項行為測試）：`--TestCaseFilter:"FullyQualifiedName!~ControlsBehavior"`
- Commit 訊息無 AI attribution（專案慣例）。

---

## File Structure

| 檔案 | 動作 | 責任 |
|------|------|------|
| `Assetra.Core/Interfaces/Analysis/ITimeWeightedReturnCalculator.cs` | 改 | 加 `ComputeSeries` 簽章 |
| `Assetra.Application/Analysis/TimeWeightedReturnCalculator.cs` | 改 | 實作 `ComputeSeries`；`Compute` 委派給它（DRY） |
| `Assetra.Tests/Analysis/TimeWeightedReturnCalculatorTests.cs` | 建/改 | `ComputeSeries` 單元測試 |
| `Assetra.WPF/Features/Portfolio/PortfolioHistoryViewModel.cs` | 改 | TWR % 線、autocomplete 狀態、chips 來源 |
| `Assetra.WPF/Features/Portfolio/ComparisonLegendItem.cs` | 改 | 加 `RemoveSymbol`（null＝不可移除） |
| `Assetra.WPF/Features/Portfolio/PortfolioViewModel.cs` | 改 | 構建 History 時傳 `services.Search` |
| `Assetra.WPF/Features/Trends/TrendsView.xaml` | 改 | chips ＋「＋比較」popup ＋ 對標表 restyle |
| `Assetra.WPF/Languages/zh-TW.xaml`, `en-US.xaml` | 改 | 新增 UI 字串（兩檔都加） |
| `Assetra.Tests/WPF/PortfolioHistoryViewModelTests.cs` | 改 | autocomplete 測試；TWR stub |

---

# Phase 0 — 我的投組線改 TWR 序列

## Task 1: `ComputeSeries` on TWR calculator

**Files:**
- Modify: `Assetra.Core/Interfaces/Analysis/ITimeWeightedReturnCalculator.cs`
- Modify: `Assetra.Application/Analysis/TimeWeightedReturnCalculator.cs`
- Test: `Assetra.Tests/Analysis/TimeWeightedReturnCalculatorTests.cs`

- [ ] **Step 1: Write failing tests**

建立/補 `Assetra.Tests/Analysis/TimeWeightedReturnCalculatorTests.cs`：

```csharp
using Assetra.Application.Analysis;
using Assetra.Core.Models.Analysis;

namespace Assetra.Tests.Analysis;

public sealed class TimeWeightedReturnCalculatorTests
{
    private static (DateOnly, decimal) P(int d, decimal v) => (new DateOnly(2026, 1, d), v);

    [Fact]
    public void ComputeSeries_FirstPointIsZero_EndpointMatchesCompute()
    {
        var calc = new TimeWeightedReturnCalculator();
        var vals = new[] { P(1, 100m), P(2, 110m), P(3, 121m) };
        var flows = System.Array.Empty<CashFlow>();

        var series = calc.ComputeSeries(vals, flows);

        Assert.NotNull(series);
        Assert.Equal(3, series!.Count);
        Assert.Equal(0m, series[0].CumulativeTwr);
        Assert.Equal(calc.Compute(vals, flows), series[^1].CumulativeTwr);
    }

    [Fact]
    public void ComputeSeries_FlowOnSellDay_DividesOut()
    {
        // day2: 100->200 = +100%；day3: 200->110 但當天 flow -90（投組角度賣出）→ segReturn 0
        var calc = new TimeWeightedReturnCalculator();
        var vals = new[] { P(1, 100m), P(2, 200m), P(3, 110m) };
        var flows = new[] { new CashFlow(new DateOnly(2026, 1, 3), -90m) };

        var series = calc.ComputeSeries(vals, flows);

        Assert.NotNull(series);
        Assert.Equal(1.0m, series![^1].CumulativeTwr); // 仍 +100%，賣出不算報酬
    }

    [Fact]
    public void ComputeSeries_BelowTwoPoints_ReturnsNull()
    {
        var calc = new TimeWeightedReturnCalculator();
        var series = calc.ComputeSeries(new[] { P(1, 100m) }, System.Array.Empty<CashFlow>());
        Assert.Null(series);
    }
}
```

- [ ] **Step 2: Run tests — verify they fail**

Run: `dotnet build Assetra.Tests/Assetra.Tests.csproj -o $env:TEMP\asmtests --no-restore -p:NuGetAudit=false`
Expected: BUILD FAIL（`ComputeSeries` 不存在）。

- [ ] **Step 3: Add interface method**

`ITimeWeightedReturnCalculator.cs`，在 `Compute` 之後加：

```csharp
    /// <summary>
    /// 與 <see cref="Compute"/> 同邏輯，但回傳每個 valuation 日的「累積 TWR」序列
    /// （首點＝0）。末點等於 <see cref="Compute"/>。少於 2 點回 null。
    /// </summary>
    IReadOnlyList<(DateOnly Date, decimal CumulativeTwr)>? ComputeSeries(
        IReadOnlyList<(DateOnly Date, decimal Value)> valuations,
        IReadOnlyList<CashFlow> flows);
```

- [ ] **Step 4: Implement + refactor `Compute` to delegate (DRY)**

`TimeWeightedReturnCalculator.cs` 整檔改為：

```csharp
using Assetra.Core.Interfaces.Analysis;
using Assetra.Core.Models.Analysis;

namespace Assetra.Application.Analysis;

public sealed class TimeWeightedReturnCalculator : ITimeWeightedReturnCalculator
{
    public decimal? Compute(
        IReadOnlyList<(DateOnly Date, decimal Value)> valuations,
        IReadOnlyList<CashFlow> flows)
    {
        var series = ComputeSeries(valuations, flows);
        return series is null ? null : series[^1].CumulativeTwr;
    }

    public IReadOnlyList<(DateOnly Date, decimal CumulativeTwr)>? ComputeSeries(
        IReadOnlyList<(DateOnly Date, decimal Value)> valuations,
        IReadOnlyList<CashFlow> flows)
    {
        ArgumentNullException.ThrowIfNull(valuations);
        ArgumentNullException.ThrowIfNull(flows);
        if (valuations.Count < 2)
            return null;

        var v = valuations.OrderBy(x => x.Date).ToArray();
        var flowByDate = flows
            .GroupBy(f => f.Date)
            .ToDictionary(g => g.Key, g => g.Sum(f => f.Amount));

        var result = new List<(DateOnly, decimal)>(v.Length) { (v[0].Date, 0m) };
        var compound = 1m;
        for (var i = 1; i < v.Length; i++)
        {
            var startV = v[i - 1].Value;
            var endV = v[i].Value;
            // Flow occurring on segment end date is treated as end-of-day (subtracted from end value).
            flowByDate.TryGetValue(v[i].Date, out var flow);
            if (startV != 0)
            {
                var segReturn = (endV - flow - startV) / startV;
                compound *= 1m + segReturn;
            }
            result.Add((v[i].Date, compound - 1m));
        }
        return result;
    }
}
```

- [ ] **Step 5: Run tests — verify pass**

Run (rebuild + test)：
`dotnet build Assetra.Tests/Assetra.Tests.csproj -o $env:TEMP\asmtests --no-restore -p:NuGetAudit=false`
`dotnet vstest $env:TEMP\asmtests\Assetra.Tests.dll --TestCaseFilter:"FullyQualifiedName~TimeWeightedReturn"`
Expected: 全部 PASS（含既有 Compute 測試 — 數學不變）。

- [ ] **Step 6: Commit**

```bash
git add Assetra.Core/Interfaces/Analysis/ITimeWeightedReturnCalculator.cs Assetra.Application/Analysis/TimeWeightedReturnCalculator.cs Assetra.Tests/Analysis/TimeWeightedReturnCalculatorTests.cs
git commit -m "feat(analysis): TWR 加 ComputeSeries（每日累積 TWR）— Phase 0 地基"
```

---

## Task 2: 我的投組對比線改吃 TWR 序列

**Files:**
- Modify: `Assetra.WPF/Features/Portfolio/PortfolioHistoryViewModel.cs`（`UpdateKpisAsync` / `TryComputeTwrAsync` 抽共用 helper、加 `BuildPortfolioTwrPercentPointsAsync`、`RefreshChartAsync` 與 `BuildChart`/`BuildComparePercentChart` 串接）

> ⚠ 先 Read 整個 `PortfolioHistoryViewModel.cs` 的 `RefreshChartAsync`（約 400–430）、`BuildChart`（約 860–880）、`UpdateKpisAsync`（483–584）、`TryComputeTwrAsync`（674–710），確認當前簽章再改。

- [ ] **Step 1: 抽共用 helper（cleaned valuations + 投組角度 flows）**

在 `PortfolioHistoryViewModel` 內新增兩個 private helper（取代 `UpdateKpisAsync` 內 inline 的剝點邏輯、與 `TryComputeTwrAsync` 內 flow 建構）：

```csharp
    /// <summary>剝掉領頭「建倉假象」低值點（median×0.05），回 cleaned (date,value) 序列。</summary>
    private static IReadOnlyList<(DateOnly Date, decimal Value)> BuildCleanedValuations(
        IReadOnlyList<PortfolioDailySnapshot> filtered)
    {
        var raw = filtered.OrderBy(s => s.SnapshotDate)
            .Select(s => (s.SnapshotDate, s.MarketValue)).ToList();
        if (raw.Count == 0) return raw;
        var median = raw.Select(s => s.MarketValue).OrderBy(v => v).ElementAt(raw.Count / 2);
        var threshold = median * 0.05m;
        var first = 0;
        while (first < raw.Count - 1 && raw[first].MarketValue < threshold) first++;
        return raw.Skip(first).ToList();
    }

    /// <summary>投組角度 cash flow（Buy 正 / Sell 負）— 既有 negate 慣例。null 服務時回空。</summary>
    private async Task<IReadOnlyList<Assetra.Core.Models.Analysis.CashFlow>> BuildPortfolioFlowsAsync(
        DateOnly start, DateOnly end)
    {
        if (_trades is null) return [];
        var allTrades = await _trades.GetAllAsync().ConfigureAwait(false);
        var period = new PerformancePeriod(start, end);
        var rawFlows = Assetra.Application.Analysis.PerformanceFlowBuilder.BuildPerformanceFlows(allTrades, period);
        return rawFlows
            .Select(f => new Assetra.Core.Models.Analysis.CashFlow(f.Date, -f.Amount, f.Currency))
            .ToList();
    }
```

然後把 `UpdateKpisAsync` 內建 `series` 的那段（約 533–543）改用 `BuildCleanedValuations(filtered)`，並把 `TryComputeTwrAsync` 內 flow 建構（約 688–705）改呼叫 `BuildPortfolioFlowsAsync(valuations[0].Date, valuations[^1].Date)`。行為不變（純抽取）。

- [ ] **Step 2: 加 TWR % 點建構方法**

```csharp
    /// <summary>
    /// 「我的投組」對比線的 TWR % 點（每日累積 TWR ×100）。服務缺 / 序列不足 → 回 null（caller fallback 裸淨值%）。
    /// </summary>
    private async Task<IReadOnlyList<DateTimePoint>?> BuildPortfolioTwrPercentPointsAsync(
        IReadOnlyList<PortfolioDailySnapshot> filtered)
    {
        if (_twr is null || _trades is null) return null;
        var vals = BuildCleanedValuations(filtered);
        if (vals.Count < 2) return null;
        var flows = await BuildPortfolioFlowsAsync(vals[0].Date, vals[^1].Date).ConfigureAwait(false);
        var series = _twr.ComputeSeries(vals, flows);
        if (series is null || series.Count < 2) return null;
        return series
            .Select(p => new DateTimePoint(p.Date.ToDateTime(TimeOnly.MinValue), (double)p.CumulativeTwr))
            .ToList();
    }
```

- [ ] **Step 3: 在 `RefreshChartAsync` % 模式分支建構 TWR 點並往下傳**

找到 % 模式 await overlays 的地方（約 415–418，刻意不 `ConfigureAwait(false)`）。在其後加：

```csharp
            overlays = await BuildBenchmarkOverlaysAsync(filtered);
            twrPoints = await BuildPortfolioTwrPercentPointsAsync(filtered); // 不加 ConfigureAwait(false)：BuildChart 需回 UI thread
```

宣告 `IReadOnlyList<DateTimePoint>? twrPoints = null;`（與 `overlays` 同範圍），並把它一路傳進 `BuildChart(...)` → `BuildComparePercentChart(...)`。

- [ ] **Step 4: `BuildComparePercentChart` 用 TWR 點取代裸正規化**

把簽章加一個參數，並改投組線資料來源：

```csharp
    private void BuildComparePercentChart(
        IReadOnlyList<DateTimePoint> points,
        IReadOnlyList<(string Label, string ColorHex, string? RemoveSymbol, IReadOnlyList<DateTimePoint> Points)>? overlays,
        IReadOnlyList<DateTimePoint>? portfolioTwrPoints,   // ← 新增
        SKColor labelColor, SKColor separatorColor)
    {
        IReadOnlyList<DateTimePoint> portfolioPct;
        if (portfolioTwrPoints is { Count: >= 2 })
        {
            portfolioPct = portfolioTwrPoints;               // TWR：與 benchmark 同基準、無假尖峰
        }
        else
        {
            var baseVal = points[0].Value ?? 0d;             // fallback：裸淨值 %（舊行為）
            portfolioPct = baseVal == 0d
                ? points
                : points.Select(p => new DateTimePoint(p.DateTime, ((p.Value ?? 0d) / baseVal) - 1d)).ToList();
        }
        // …其餘建線/圖例邏輯不變（注意 overlays tuple 多了 RemoveSymbol，見 Task 5）…
    }
```

（`overlays` tuple 的 `RemoveSymbol` 欄位在 Task 5 一起加；本 Task 先讓編譯過可暫不用它。）

- [ ] **Step 5: Build + 全套測試**

Run：
`dotnet build Assetra.Tests/Assetra.Tests.csproj -o $env:TEMP\asmtests --no-restore -p:NuGetAudit=false`
`dotnet vstest $env:TEMP\asmtests\Assetra.Tests.dll --TestCaseFilter:"FullyQualifiedName!~ControlsBehavior"`
Expected: 全綠（1736＋）。既有 KPI/TWR 行為不變、新增路徑有 fallback。

- [ ] **Step 6: Commit + 交付驗證**

```bash
git add Assetra.WPF/Features/Portfolio/PortfolioHistoryViewModel.cs
git commit -m "feat(portfolio): 我的投組對比線改 TWR 序列 — 消除建倉/賣股假尖峰（Phase 0）"
```

**交付使用者目視驗證：** 重 build app → 資產趨勢 → 勾「vs 大盤」→「我的投組」不再衝 +150%、benchmark（加權指數/0050）看得出來。**等使用者確認再進 Phase 1。**

---

# Phase 1 — Google 式對比 UI

## Task 3: 注入 `IStockSearchService` 到 VM

**Files:**
- Modify: `Assetra.WPF/Features/Portfolio/PortfolioHistoryViewModel.cs`（ctor ＋ 欄位）
- Modify: `Assetra.WPF/Features/Portfolio/PortfolioViewModel.cs`（構建 History 處）

- [ ] **Step 1: 加欄位 + ctor 參數**

`PortfolioHistoryViewModel`：宣告 `private readonly IStockSearchService? _search;`，ctor 末端加參數 `IStockSearchService? search = null`（放在 `concentration` 之後），body 加 `_search = search;`。

- [ ] **Step 2: 構建處傳入**

`PortfolioViewModel.cs` 約 456–467 `new PortfolioHistoryViewModel(...)` 參數串尾端（`services.Concentration` 之後）加 `services.Search`。

- [ ] **Step 3: Build — 確認不破**

Run build（同上）。Expected: 0 error（既有測試以 `null` 預設值構建，不受影響）。

- [ ] **Step 4: Commit**

```bash
git add Assetra.WPF/Features/Portfolio/PortfolioHistoryViewModel.cs Assetra.WPF/Features/Portfolio/PortfolioViewModel.cs
git commit -m "chore(portfolio): 注入 IStockSearchService 到 PortfolioHistoryViewModel（Phase 1 前置）"
```

---

## Task 4: autocomplete 狀態 + 選取命令

**Files:**
- Modify: `Assetra.WPF/Features/Portfolio/PortfolioHistoryViewModel.cs`
- Test: `Assetra.Tests/WPF/PortfolioHistoryViewModelTests.cs`

- [ ] **Step 1: Write failing test**

`PortfolioHistoryViewModelTests.cs` 加（需要一個 search stub；若檔內無，於檔末加 `StubStockSearch`）：

```csharp
    private sealed class StubStockSearch(params StockSearchResult[] all) : IStockSearchService
    {
        public IReadOnlyList<StockSearchResult> Search(string query) =>
            all.Where(r => r.Symbol.Contains(query, StringComparison.OrdinalIgnoreCase)
                        || r.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        public IReadOnlyList<StockSearchResult> GetAll() => all;
        public string? GetExchange(string symbol) => all.FirstOrDefault(r => r.Symbol == symbol)?.Exchange;
        public string? GetName(string symbol) => all.FirstOrDefault(r => r.Symbol == symbol)?.Name;
        public string? GetSector(string symbol) => null;
        public bool IsEtf(string symbol) => false;
        public bool IsBondEtf(string symbol) => false;
    }

    [Fact]
    public void TypingComparisonInput_PopulatesSuggestions()
    {
        var search = new StubStockSearch(
            new StockSearchResult("0050", "元大台灣50", "TWSE"),
            new StockSearchResult("2330", "台積電", "TWSE"));
        var vm = new PortfolioHistoryViewModel(
            new StubHistoryQuery(System.Array.Empty<PortfolioDailySnapshot>()),
            search: search);

        vm.ComparisonInput = "0050";

        Assert.Single(vm.ComparisonSuggestions);
        Assert.Equal("0050", vm.ComparisonSuggestions[0].Symbol);
    }
```

（`StockSearchResult` 建構子第 4 個之後參數若有預設值可省略；否則補齊。先 Read `Assetra.Core/Models/StockSearchResult.cs` 確認。`new PortfolioHistoryViewModel(...)` 的具名參數 `search:` 需對應 Task 3 的 ctor。）

- [ ] **Step 2: Run — verify fail**

Run vstest filter `~PortfolioHistoryViewModel`。Expected: 編譯失敗（`ComparisonSuggestions` 不存在）。

- [ ] **Step 3: Implement autocomplete state**

`PortfolioHistoryViewModel`，在 `OnComparisonInputChanged`（約 87）擴充並加新成員：

```csharp
    [ObservableProperty] private IReadOnlyList<StockSearchResult> _comparisonSuggestions = [];
    [ObservableProperty] private bool _isComparisonPickerOpen;

    partial void OnComparisonInputChanged(string value)
    {
        OnPropertyChanged(nameof(CanAddComparison));
        AddComparisonCommand.NotifyCanExecuteChanged();
        ComparisonSuggestions = string.IsNullOrWhiteSpace(value) || _search is null
            ? []
            : _search.Search(value.Trim()).Take(8).ToList();
    }

    [RelayCommand]
    private void OpenComparisonPicker()
    {
        ComparisonInput = string.Empty;
        ComparisonSuggestions = [];
        IsComparisonPickerOpen = true;
    }

    [RelayCommand]
    private async Task SelectComparisonSuggestionAsync(StockSearchResult? r)
    {
        if (_settings is null || r is null) return;
        var symbol = FormatBenchmarkSymbol(r);
        if (CurrentCustomBenchmarks.Count >= 4
            || CurrentCustomBenchmarks.Any(s => string.Equals(s, symbol, StringComparison.OrdinalIgnoreCase)))
        { IsComparisonPickerOpen = false; return; }
        var list = CurrentCustomBenchmarks.ToList();
        list.Add(symbol);
        await PersistCustomBenchmarksAsync(list).ConfigureAwait(true);
        ComparisonInput = string.Empty;
        ComparisonSuggestions = [];
        IsComparisonPickerOpen = false;
        RefreshChart();
    }

    /// <summary>把搜尋結果轉成 benchmark 抓取用 symbol：TWSE→.TW、TPEX→.TWO、其餘原樣。</summary>
    private static string FormatBenchmarkSymbol(StockSearchResult r) => r.Exchange switch
    {
        "TWSE" => $"{r.Symbol}.TW",
        "TPEX" => $"{r.Symbol}.TWO",
        _ => r.Symbol,
    };
```

- [ ] **Step 4: Run — verify pass**

Run vstest filter `~PortfolioHistoryViewModel`。Expected: PASS。

- [ ] **Step 5: Commit**

```bash
git add Assetra.WPF/Features/Portfolio/PortfolioHistoryViewModel.cs Assetra.Tests/WPF/PortfolioHistoryViewModelTests.cs
git commit -m "feat(portfolio): 對比 picker autocomplete 狀態＋選取命令（Phase 1）"
```

---

## Task 5: 可移除 chips（`RemoveSymbol` 串接）

**Files:**
- Modify: `Assetra.WPF/Features/Portfolio/ComparisonLegendItem.cs`
- Modify: `Assetra.WPF/Features/Portfolio/PortfolioHistoryViewModel.cs`（`BuildBenchmarkOverlaysAsync` tuple、`BuildComparePercentChart` legend）

- [ ] **Step 1: 擴充 record**

`ComparisonLegendItem.cs`：

```csharp
public sealed record ComparisonLegendItem(string Label, string ColorHex, string? RemoveSymbol = null);
```

（`RemoveSymbol` null＝固定項：我的投組、加權指數；非 null＝自訂對標，chip 顯示 ✕。）

- [ ] **Step 2: overlays tuple 帶 RemoveSymbol**

`BuildBenchmarkOverlaysAsync`（約 927–962）回傳型別與 `BuildComparePercentChart` 參數型別都加 `string? RemoveSymbol`。`specs[0]`＝加權指數（RemoveSymbol null）、`specs[1..]`＝自訂（RemoveSymbol＝該 symbol）：

```csharp
        // overlays.Add(...) 那行改為帶 RemoveSymbol：
        var removeSymbol = i == 0 ? null : specs[i].Symbol; // [0]=加權指數固定，其餘=自訂可移除
        overlays.Add((specs[i].Label, ComparisonPalette[(i + 1) % ComparisonPalette.Length], removeSymbol, pts));
```

回傳宣告型別同步改為 `List<(string, string, string?, IReadOnlyList<DateTimePoint>)>`。

- [ ] **Step 3: legend 帶 RemoveSymbol**

`BuildComparePercentChart` 內：我的投組那筆 `new(meLabel, meColorHex)` 維持（RemoveSymbol 預設 null）；overlay 迴圈解構成 `(label, colorHex, removeSymbol, pts)`，`legend.Add(new ComparisonLegendItem(label, colorHex, removeSymbol));`。

- [ ] **Step 4: Build + 全套測試**

Run build + `--TestCaseFilter:"FullyQualifiedName!~ControlsBehavior"`。Expected: 全綠。

- [ ] **Step 5: Commit**

```bash
git add Assetra.WPF/Features/Portfolio/ComparisonLegendItem.cs Assetra.WPF/Features/Portfolio/PortfolioHistoryViewModel.cs
git commit -m "feat(portfolio): 對比圖例帶 RemoveSymbol（chips 可移除來源）（Phase 1）"
```

---

## Task 6: `TrendsView.xaml` — chips ＋「＋比較」popup ＋ 對標表

**Files:**
- Modify: `Assetra.WPF/Features/Trends/TrendsView.xaml`
- Modify: `Assetra.WPF/Languages/zh-TW.xaml`, `Assetra.WPF/Languages/en-US.xaml`

> ⚠ 先 Read 現有 `TrendsView.xaml`：定位（a）目前左上的 `ComparisonLegend` ItemsControl、（b）底部「新增比較對象」TextBox＋Button 列、（c）`同期對標報酬率` 區。以下為「目標」片段，套用到對應位置。

- [ ] **Step 1: chips（取代左上小圖例）**

把 `ComparisonLegend` 的 ItemsControl 改成 chip（色點＋label＋條件式 ✕）：

```xml
<ItemsControl ItemsSource="{Binding ComparisonLegend}" Visibility="{Binding IsComparePercentMode, Converter={StaticResource BoolToVis}}">
  <ItemsControl.ItemsPanel>
    <ItemsPanelTemplate><StackPanel Orientation="Horizontal" /></ItemsPanelTemplate>
  </ItemsControl.ItemsPanel>
  <ItemsControl.ItemTemplate>
    <DataTemplate>
      <Border Margin="0,0,8,0" Padding="8,3" CornerRadius="11" Background="{DynamicResource AppSurfaceAlt}">
        <StackPanel Orientation="Horizontal">
          <Ellipse Width="8" Height="8" VerticalAlignment="Center" Margin="0,0,6,0"
                   Fill="{Binding ColorHex}" />
          <TextBlock Text="{Binding Label}" VerticalAlignment="Center"
                     FontSize="{StaticResource Font.Size.Xs}" Foreground="{DynamicResource AppTextPrimary}" />
          <Button Margin="6,0,0,0" Padding="0" Content="✕" FontSize="10"
                  Background="Transparent" BorderThickness="0" Cursor="Hand"
                  Command="{Binding DataContext.RemoveComparisonCommand, RelativeSource={RelativeSource AncestorType=ItemsControl}}"
                  CommandParameter="{Binding RemoveSymbol}"
                  Visibility="{Binding RemoveSymbol, Converter={StaticResource NullToCollapsed}}" />
        </StackPanel>
      </Border>
    </DataTemplate>
  </ItemsControl.ItemTemplate>
</ItemsControl>
```

> `ColorHex` 是 hex 字串 → 需要 string→Brush converter。若專案已有（grep `StringToBrush`/`HexToBrush`）就用既有；否則 chip 色點改綁既有作法或新增一個 converter（先 grep 確認，避免重複造）。`NullToCollapsed`／`BoolToVis` 同樣先 grep 既有 converter key。

- [ ] **Step 2:「＋比較」按鈕 + 搜尋 popup（取代底部 TextBox＋Button 列）**

```xml
<StackPanel Orientation="Horizontal">
  <ToggleButton x:Name="CompareBtn" Content="{DynamicResource Portfolio.History.AddComparison}"
                IsChecked="{Binding IsComparisonPickerOpen}" Style="{StaticResource ...既有小按鈕樣式}" />
  <Popup IsOpen="{Binding IsComparisonPickerOpen}" PlacementTarget="{Binding ElementName=CompareBtn}"
         Placement="Bottom" StaysOpen="False" AllowsTransparency="True">
    <Border Width="280" Padding="8" CornerRadius="8" Background="{DynamicResource AppSurface}"
            BorderBrush="{DynamicResource AppBorder}" BorderThickness="1">
      <StackPanel>
        <TextBox Text="{Binding ComparisonInput, UpdateSourceTrigger=PropertyChanged}"
                 Tag="{DynamicResource Portfolio.History.AddComparison.Hint}" />
        <ItemsControl ItemsSource="{Binding ComparisonSuggestions}" MaxHeight="240">
          <ItemsControl.ItemTemplate>
            <DataTemplate>
              <Button HorizontalContentAlignment="Stretch" Background="Transparent" BorderThickness="0" Cursor="Hand"
                      Command="{Binding DataContext.SelectComparisonSuggestionCommand, RelativeSource={RelativeSource AncestorType=ItemsControl}}"
                      CommandParameter="{Binding}">
                <Grid>
                  <Grid.ColumnDefinitions><ColumnDefinition Width="*" /><ColumnDefinition Width="Auto" /></Grid.ColumnDefinitions>
                  <StackPanel>
                    <TextBlock Text="{Binding Symbol}" FontWeight="SemiBold" />
                    <TextBlock Text="{Binding Name}" FontSize="{StaticResource Font.Size.Xs}"
                               Foreground="{DynamicResource AppTextSecondary}" />
                  </StackPanel>
                  <TextBlock Grid.Column="1" Text="{Binding Exchange}" VerticalAlignment="Center"
                             FontSize="{StaticResource Font.Size.Xs}" Foreground="{DynamicResource AppTextSecondary}" />
                </Grid>
              </Button>
            </DataTemplate>
          </ItemsControl.ItemTemplate>
        </ItemsControl>
      </StackPanel>
    </Border>
  </Popup>
</StackPanel>
```

> 保留「手打代號加入」：TextBox 綁 `ComparisonInput`，可在 Popup 內加一顆「＋」Button 綁既有 `AddComparisonCommand`（給指數 `^TWII`/`SOX` 這種搜尋庫沒有的）。

- [ ] **Step 3: 對標表 restyle（同期對標報酬率區）**

維持既有 4 固定 benchmark ＋ 自訂列，但每列前置色點（對齊 chips 色）。最小改動：每列加 `<Ellipse Width="8" Height="8" .../>`。最上面加「我的投組」列（值＝`KpiReturnPct`，F2 百分比）。若工時超預算可標記為 follow-up，不阻擋 Task 完成。

- [ ] **Step 4: i18n keys（兩檔都加）**

`zh-TW.xaml` / `en-US.xaml` 補（若 Phase 2a/2b 已加 `Portfolio.History.AddComparison` / `.Hint` 則沿用，不重複）：

| key | zh-TW | en-US |
|-----|-------|-------|
| `Portfolio.History.AddComparison` | ＋比較 | + Compare |
| `Portfolio.History.AddComparison.Hint` | 搜尋代號／名稱… | Search symbol / name… |

- [ ] **Step 5: Build + 全套測試**

Run build + `--TestCaseFilter:"FullyQualifiedName!~ControlsBehavior"`。Expected: 全綠（XAML 改動不影響單元測試，但確保 VM 綁定名稱正確、編譯過）。

- [ ] **Step 6: Commit + 交付驗證**

```bash
git add Assetra.WPF/Features/Trends/TrendsView.xaml Assetra.WPF/Languages/zh-TW.xaml Assetra.WPF/Languages/en-US.xaml
git commit -m "feat(portfolio): 資產趨勢對比改 Google 式 chips＋搜尋 popup（Phase 1）"
```

**交付使用者目視驗證：** 重 build app → 資產趨勢 → chips 一排可見、自訂對標有 ✕ 可移除；點「＋比較」→ popup 搜尋 → 邊打邊出預覽 → 選取加入一條線＋一個 chip。

---

## Self-Review（plan vs spec）

- **Spec §3 Phase 0（TWR 序列）** → Task 1（ComputeSeries）＋ Task 2（VM 串接、fallback）。✅
- **Spec §3 Phase 1 1a chips** → Task 5（RemoveSymbol）＋ Task 6 Step 1。✅
- **Spec §3 Phase 1 1b 搜尋 popup** → Task 3（DI）＋ Task 4（autocomplete）＋ Task 6 Step 2。✅
- **Spec §3 Phase 1 1c 對標表** → Task 6 Step 3。✅
- **Spec §5 錯誤處理（服務缺 fallback）** → Task 2 Step 4（fallback 裸淨值%）、Task 4（_search null → 空 suggestions）。✅
- **Spec §6 測試** → Task 1 Step 1、Task 4 Step 1。✅
- **型別一致**：`ComputeSeries` 回 `(DateOnly, decimal CumulativeTwr)`；`ComparisonLegendItem(Label, ColorHex, RemoveSymbol?)`；overlays tuple 四元素一致（Task 2 Step 4 與 Task 5 Step 2 對齊）。✅
- **未決小項（執行時 grep 確認，非阻擋）**：string→Brush converter、`NullToCollapsed`/`BoolToVis` converter key 是否已存在；對標表色點為 follow-up 可延後。
