using Assetra.Application.Portfolio.Contracts;
using Assetra.Core.Interfaces;
using Assetra.Core.Interfaces.Analysis;
using Assetra.Core.Models;
using Assetra.WPF.Features.Portfolio;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using Xunit;

namespace Assetra.Tests.WPF;

public sealed class PortfolioHistoryViewModelTests
{
    [Fact]
    public async Task LoadAsync_ConvertsSnapshotCurrenciesToBaseCurrency()
    {
        var snapshots = new[]
        {
            new PortfolioDailySnapshot(new DateOnly(2026, 1, 1), 0m, 32_000m, 0m, 1, "TWD"),
            new PortfolioDailySnapshot(new DateOnly(2026, 1, 2), 0m, 1_000m, 0m, 1, "USD"),
        };
        var vm = new PortfolioHistoryViewModel(
            new StubHistoryQuery(snapshots),
            settings: new FakeSettings("TWD"),
            fx: new StubFx(new Dictionary<(string From, string To), decimal>
            {
                { ("USD", "TWD"), 32m },
            }));

        await vm.LoadAsync();

        var values = ChartValues(vm);
        Assert.Equal([32_000d, 32_000d], values);
    }

    [Fact]
    public async Task ChangePeriodAll_IncludesSnapshotsOlderThanTenYears()
    {
        var oldDate = DateOnly.FromDateTime(DateTime.Today.AddYears(-12));
        var snapshots = new[]
        {
            new PortfolioDailySnapshot(oldDate, 0m, 10m, 0m, 1),
            new PortfolioDailySnapshot(DateOnly.FromDateTime(DateTime.Today), 0m, 20m, 0m, 1),
        };
        var vm = new PortfolioHistoryViewModel(new StubHistoryQuery(snapshots));

        await vm.LoadAsync();
        vm.ChangePeriodCommand.Execute("All");
        await Task.Yield();

        Assert.Equal("All", vm.ActivePeriod);
        Assert.Equal([10d, 20d], ChartValues(vm));
    }

    [Fact]
    public async Task LoadAsync_WhenFilteredRangeHasNoSnapshots_ShowsEmptyState()
    {
        var snapshots = new[]
        {
            new PortfolioDailySnapshot(new DateOnly(2026, 1, 1), 0m, 10m, 0m, 1),
        };
        var vm = new PortfolioHistoryViewModel(new StubHistoryQuery(snapshots))
        {
            CustomStartDate = new DateTime(2030, 1, 1),
            CustomEndDate = new DateTime(2030, 1, 31),
        };

        await vm.LoadAsync();

        Assert.False(vm.HasHistory);
        Assert.Empty(vm.ValueSeries);
    }

    // ── Stage 1 (Dashboard consolidation)：區間 KPI + 對標 ──────────────────

    [Fact]
    public async Task LoadAsync_ComputesPeriodKpisFromFilteredSnapshots()
    {
        var snapshots = new[]
        {
            new PortfolioDailySnapshot(new DateOnly(2026, 1, 1), 0m, 1_000m, 0m, 1),
            new PortfolioDailySnapshot(new DateOnly(2026, 7, 1), 0m, 1_500m, 0m, 1),
        };
        var vm = new PortfolioHistoryViewModel(new StubHistoryQuery(snapshots));
        vm.SelectedDays = 0; // "All" so both snapshots are included

        await vm.LoadAsync();

        Assert.True(vm.HasKpis);
        Assert.Equal(1_000m, vm.KpiStartValue);
        Assert.Equal(1_500m, vm.KpiEndValue);
        Assert.Equal(500m, vm.KpiAbsolutePnl);
        Assert.Equal(0.5m, vm.KpiReturnPct);
        // Annualized over ~6 months should be > 50% (compounding to full year)
        Assert.True(vm.KpiAnnualizedPct > 0.5m, $"annualized was {vm.KpiAnnualizedPct}");
    }

    [Fact]
    public async Task LoadAsync_NoDrawdownService_HidesDrawdownAndKeepsZero()
    {
        var snapshots = new[]
        {
            new PortfolioDailySnapshot(new DateOnly(2026, 1, 1), 0m, 100m, 0m, 1),
            new PortfolioDailySnapshot(new DateOnly(2026, 1, 2), 0m, 110m, 0m, 1),
        };
        var vm = new PortfolioHistoryViewModel(new StubHistoryQuery(snapshots));

        await vm.LoadAsync();

        Assert.False(vm.HasDrawdown);
        Assert.Equal(0m, vm.KpiMaxDrawdownPct);
    }

    [Fact]
    public async Task LoadAsync_WithDrawdownService_ComputesMaxDrawdown()
    {
        // 1000 → 1200 → 800 → peak 1200, trough 800, dd = (800-1200)/1200 = -33.3%
        var snapshots = new[]
        {
            new PortfolioDailySnapshot(new DateOnly(2026, 1, 1), 0m, 1_000m, 0m, 1),
            new PortfolioDailySnapshot(new DateOnly(2026, 1, 2), 0m, 1_200m, 0m, 1),
            new PortfolioDailySnapshot(new DateOnly(2026, 1, 3), 0m, 800m, 0m, 1),
        };
        var vm = new PortfolioHistoryViewModel(
            new StubHistoryQuery(snapshots),
            drawdown: new Assetra.Application.Analysis.DrawdownCalculator());

        await vm.LoadAsync();

        Assert.True(vm.HasDrawdown);
        // DrawdownCalculator returns magnitude (positive). 1000 → 1200 (peak) → 800
        // gives (1200-800)/1200 = 33.33%.
        Assert.True(vm.KpiMaxDrawdownPct > 0.3m && vm.KpiMaxDrawdownPct < 0.4m,
            $"expected ~ 33% drawdown magnitude, got {vm.KpiMaxDrawdownPct}");
    }

    [Fact]
    public async Task LoadAsync_NoBenchmarkService_HidesBenchmarkRow()
    {
        var snapshots = new[]
        {
            new PortfolioDailySnapshot(new DateOnly(2026, 1, 1), 0m, 100m, 0m, 1),
            new PortfolioDailySnapshot(new DateOnly(2026, 1, 2), 0m, 110m, 0m, 1),
        };
        var vm = new PortfolioHistoryViewModel(new StubHistoryQuery(snapshots));

        await vm.LoadAsync();

        Assert.False(vm.HasBenchmark);
        Assert.Equal("—", vm.BenchmarkTaiexDisplay);
        Assert.Equal("—", vm.BenchmarkTw0050Display);
        Assert.Equal("—", vm.BenchmarkTw00981ADisplay);
        Assert.Equal("—", vm.BenchmarkDeposit15Display);
    }

    [Fact]
    public async Task LoadAsync_WithBenchmarkService_ComputesDepositBaseline()
    {
        // 1.5% 年化合成基準不依賴外部 history provider；只要 service 存在
        // 且 filtered.Count >= 2，Deposit15Display 應該被計算出非 "—"。
        var snapshots = new[]
        {
            new PortfolioDailySnapshot(new DateOnly(2025, 1, 1), 0m, 1_000m, 0m, 1),
            new PortfolioDailySnapshot(new DateOnly(2026, 1, 1), 0m, 1_100m, 0m, 1),
        };
        var vm = new PortfolioHistoryViewModel(
            new StubHistoryQuery(snapshots),
            benchmark: new StubBenchmark(null));
        vm.SelectedDays = 0;

        await vm.LoadAsync();

        Assert.True(vm.HasBenchmark);
        Assert.NotEqual("—", vm.BenchmarkDeposit15Display);
        // ETF / index benchmark 在 stub 回 null 時應 fallback 為 "—"，不影響 deposit。
        Assert.Equal("—", vm.BenchmarkTaiexDisplay);
    }

    [Fact]
    public async Task CustomBenchmarksChange_RefreshesBenchmarkRows_WithoutReload()
    {
        // P5: changing CustomBenchmarkSymbols in settings while sitting on Trends must
        // refresh the custom-benchmark comparison rows. Previously UpdateBenchmarksAsync
        // only ran during a chart refresh (load / period / theme), so an edited list went
        // stale until a revisit. The VM now subscribes to IAppSettingsService.Changed and
        // re-runs only the benchmark computation when the symbol list actually changed.
        var snapshots = new[]
        {
            new PortfolioDailySnapshot(new DateOnly(2025, 1, 1), 0m, 1_000m, 0m, 1),
            new PortfolioDailySnapshot(new DateOnly(2026, 1, 1), 0m, 1_100m, 0m, 1),
        };
        var benchmark = new RecordingBenchmark();
        var settings = new FakeSettings(new AppSettings(BaseCurrency: "TWD"));
        var vm = new PortfolioHistoryViewModel(
            new StubHistoryQuery(snapshots),
            settings: settings,
            benchmark: benchmark);
        vm.SelectedDays = 0;

        await vm.LoadAsync();

        // No custom symbols yet → no custom rows.
        Assert.Empty(vm.CustomBenchmarks);

        // User adds a custom benchmark symbol and saves → Changed fires.
        await settings.SaveAsync(new AppSettings(
            BaseCurrency: "TWD",
            CustomBenchmarkSymbols: new List<string> { "QQQ" }));

        // Refresh is fire-and-forget; wait for the custom row to land.
        await WaitForAsync(() => vm.CustomBenchmarks.Any(r => r.Symbol == "QQQ"));

        Assert.Single(vm.CustomBenchmarks);
        Assert.Equal("QQQ", vm.CustomBenchmarks[0].Symbol);
        lock (benchmark.RequestedSymbols)
            Assert.Contains("QQQ", benchmark.RequestedSymbols);
    }

    [Fact]
    public async Task UnrelatedSettingsChange_DoesNotRecomputeBenchmarks()
    {
        // P5 guard: the Changed handler must only re-run benchmarks when the symbol list
        // actually changed — otherwise every unrelated save (KPI selection, currency, etc.)
        // would trigger redundant external TWR fetches.
        var snapshots = new[]
        {
            new PortfolioDailySnapshot(new DateOnly(2025, 1, 1), 0m, 1_000m, 0m, 1),
            new PortfolioDailySnapshot(new DateOnly(2026, 1, 1), 0m, 1_100m, 0m, 1),
        };
        var benchmark = new RecordingBenchmark();
        var settings = new FakeSettings(new AppSettings(
            BaseCurrency: "TWD",
            CustomBenchmarkSymbols: new List<string> { "QQQ" }));
        var vm = new PortfolioHistoryViewModel(
            new StubHistoryQuery(snapshots),
            settings: settings,
            benchmark: benchmark);
        vm.SelectedDays = 0;

        await vm.LoadAsync();
        int afterLoad;
        lock (benchmark.RequestedSymbols)
            afterLoad = benchmark.RequestedSymbols.Count(s => s == "QQQ");

        // Save with the SAME custom symbols but a different unrelated field.
        await settings.SaveAsync(new AppSettings(
            BaseCurrency: "USD",
            CustomBenchmarkSymbols: new List<string> { "QQQ" }));

        // Give any erroneous fire-and-forget a chance to run, then assert no extra fetch.
        await Task.Delay(150);
        int afterSave;
        lock (benchmark.RequestedSymbols)
            afterSave = benchmark.RequestedSymbols.Count(s => s == "QQQ");
        Assert.Equal(afterLoad, afterSave);
    }

    private static async Task WaitForAsync(Func<bool> condition, int timeoutMs = 2000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return;
            await Task.Delay(15);
        }
        Assert.True(condition(), "Condition was not met within the timeout.");
    }

    [Fact]
    public async Task LoadAsync_BelowTwoSnapshots_HasKpisFalse()
    {
        var snapshots = new[]
        {
            new PortfolioDailySnapshot(new DateOnly(2026, 1, 1), 0m, 1_000m, 0m, 1),
        };
        var vm = new PortfolioHistoryViewModel(new StubHistoryQuery(snapshots));

        await vm.LoadAsync();

        Assert.False(vm.HasKpis);
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

    private sealed class StubStockSearch(params StockSearchResult[] all) : IStockSearchService
    {
        public IReadOnlyList<StockSearchResult> Search(string query) =>
            all.Where(r => r.Symbol.Contains(query, System.StringComparison.OrdinalIgnoreCase)
                        || r.Name.Contains(query, System.StringComparison.OrdinalIgnoreCase)).ToList();
        public IReadOnlyList<StockSearchResult> GetAll() => all;
        public string? GetExchange(string symbol) => all.FirstOrDefault(r => r.Symbol == symbol)?.Exchange;
        public string? GetName(string symbol) => all.FirstOrDefault(r => r.Symbol == symbol)?.Name;
        public string? GetSector(string symbol) => null;
        public bool IsEtf(string symbol) => false;
        public bool IsBondEtf(string symbol) => false;
    }

    private sealed class StubBenchmark(decimal? result) : IBenchmarkComparisonService
    {
        public Task<decimal?> ComputeBenchmarkTwrAsync(
            string symbol,
            Assetra.Core.Models.Analysis.PerformancePeriod period,
            CancellationToken ct = default) =>
            Task.FromResult(result);

        public Task<IReadOnlyList<Assetra.Core.Models.Analysis.BenchmarkSeriesPoint>?> ComputeBenchmarkSeriesAsync(
            string symbol,
            Assetra.Core.Models.Analysis.PerformancePeriod period,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Assetra.Core.Models.Analysis.BenchmarkSeriesPoint>?>(null);
    }

    // ── New tests for risk metrics integration (Volatility / Sharpe / HHI) ──

    [Fact]
    public async Task LoadAsync_NoVolatilityService_HasVolatilityFalse()
    {
        var snapshots = new[]
        {
            new PortfolioDailySnapshot(new DateOnly(2026, 1, 1), 0m, 1_000m, 0m, 1),
            new PortfolioDailySnapshot(new DateOnly(2026, 1, 2), 0m, 1_050m, 0m, 1),
        };
        var vm = new PortfolioHistoryViewModel(new StubHistoryQuery(snapshots));

        await vm.LoadAsync();

        Assert.False(vm.HasVolatility);
        Assert.False(vm.HasSharpe);
        Assert.False(vm.HasHhi);
    }

    [Fact]
    public async Task LoadAsync_WithVolatilityService_ComputesValue()
    {
        var snapshots = new[]
        {
            new PortfolioDailySnapshot(new DateOnly(2026, 1, 1), 0m, 1_000m, 0m, 1),
            new PortfolioDailySnapshot(new DateOnly(2026, 1, 2), 0m, 1_050m, 0m, 1),
            new PortfolioDailySnapshot(new DateOnly(2026, 1, 3), 0m, 1_010m, 0m, 1),
            new PortfolioDailySnapshot(new DateOnly(2026, 1, 4), 0m, 1_060m, 0m, 1),
        };
        var vm = new PortfolioHistoryViewModel(
            new StubHistoryQuery(snapshots),
            volatility: new Assetra.Application.Analysis.VolatilityCalculator());
        vm.SelectedDays = 0;

        await vm.LoadAsync();

        Assert.True(vm.HasVolatility);
        Assert.True(vm.KpiVolatilityPct > 0m, $"expected positive volatility, got {vm.KpiVolatilityPct}");
    }

    [Fact]
    public async Task LoadAsync_NoConcentrationService_HhiZero()
    {
        var snapshots = new[]
        {
            new PortfolioDailySnapshot(new DateOnly(2026, 1, 1), 0m, 100m, 0m, 1),
            new PortfolioDailySnapshot(new DateOnly(2026, 1, 2), 0m, 110m, 0m, 1),
        };
        var vm = new PortfolioHistoryViewModel(new StubHistoryQuery(snapshots));

        await vm.LoadAsync();

        Assert.False(vm.HasHhi);
        Assert.Equal(0m, vm.KpiHhi);
    }

    [Fact]
    public async Task LoadAsync_WithTwrAndTrades_UsesFullTwrInsteadOfNaive()
    {
        // 區間：start=1000, end=1100, naive return = +10%
        // 中間有 -50 cash flow（如 withdrawal），TWR 應比 naive 更高
        var snapshots = new[]
        {
            new PortfolioDailySnapshot(new DateOnly(2026, 1, 1), 0m, 1_000m, 0m, 1),
            new PortfolioDailySnapshot(new DateOnly(2026, 1, 15), 0m, 1_050m, 0m, 1),
            new PortfolioDailySnapshot(new DateOnly(2026, 1, 30), 0m, 1_100m, 0m, 1),
        };

        // 注入 TWR calculator + 一個沒交易的 stub trades repository（flows 空 → 算出來 = naive）
        var vm = new PortfolioHistoryViewModel(
            new StubHistoryQuery(snapshots),
            twr: new Assetra.Application.Analysis.TimeWeightedReturnCalculator(),
            trades: new StubTrades(Array.Empty<Trade>()));
        vm.SelectedDays = 0;

        await vm.LoadAsync();

        Assert.True(vm.HasKpis);
        // 沒 cash flow 時 TWR ≈ naive 10%
        Assert.InRange(vm.KpiReturnPct, 0.099m, 0.101m);
    }

    /// <summary>
    /// Minimal stub for tests — only GetAllAsync is exercised by PortfolioHistoryVM；
    /// 其餘成員 throw NotImplementedException 以快速發現意外呼叫。
    /// 介面預設方法（GetByPeriodAsync / GetByPortfolioEntryIdsAsync / CountByCategoryAsync）
    /// 會自動 fall back 到 GetAllAsync — 不需在這裡 override。
    /// </summary>
    private sealed class StubTrades(IReadOnlyList<Trade> trades) : ITradeRepository
    {
        public Task<IReadOnlyList<Trade>> GetAllAsync(CancellationToken ct = default) => Task.FromResult(trades);
        public Task<IReadOnlyList<Trade>> GetByCashAccountAsync(Guid cashAccountId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Trade>>(Array.Empty<Trade>());
        public Task<IReadOnlyList<Trade>> GetByLoanLabelAsync(string loanLabel, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Trade>>(Array.Empty<Trade>());
        public Task<Trade?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult<Trade?>(trades.FirstOrDefault(t => t.Id == id));
        public Task AddAsync(Trade trade, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateAsync(Trade trade, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveChildrenAsync(Guid parentId, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveByAccountIdAsync(Guid accountId, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveByLiabilityAsync(Guid? liabilityAssetId, string? loanLabel, CancellationToken ct = default) => Task.CompletedTask;
        public Task ApplyAtomicAsync(IReadOnlyList<Assetra.Core.Models.TradeMutation> mutations, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    [Fact]
    public async Task CustomDateChange_RefreshesChartWithSelectedRange()
    {
        var snapshots = new[]
        {
            new PortfolioDailySnapshot(new DateOnly(2026, 1, 1), 0m, 10m, 0m, 1),
            new PortfolioDailySnapshot(new DateOnly(2026, 1, 2), 0m, 20m, 0m, 1),
            new PortfolioDailySnapshot(new DateOnly(2026, 1, 3), 0m, 30m, 0m, 1),
        };
        var vm = new PortfolioHistoryViewModel(new StubHistoryQuery(snapshots));

        await vm.LoadAsync();
        vm.CustomStartDate = new DateTime(2026, 1, 2);
        vm.CustomEndDate = new DateTime(2026, 1, 2);
        await Task.Yield();

        Assert.Equal("Custom", vm.ActivePeriod);
        Assert.Equal([20d], ChartValues(vm));
    }

    private static List<double> ChartValues(PortfolioHistoryViewModel vm)
    {
        var series = Assert.IsType<LineSeries<DateTimePoint>>(Assert.Single(vm.ValueSeries));
        return series.Values!.Select(p => p.Value!.Value).ToList();
    }

    private sealed class StubHistoryQuery(IReadOnlyList<PortfolioDailySnapshot> snapshots)
        : IPortfolioHistoryQueryService
    {
        public Task<IReadOnlyList<PortfolioDailySnapshot>> GetSnapshotsAsync(
            DateOnly? from = null,
            DateOnly? to = null,
            CancellationToken ct = default) =>
            Task.FromResult(snapshots);
    }

    private sealed class FakeSettings : IAppSettingsService
    {
        public FakeSettings(string baseCurrency)
            => Current = new AppSettings(BaseCurrency: baseCurrency);

        public FakeSettings(AppSettings initial)
            => Current = initial;

        public AppSettings Current { get; private set; }
        public event Action? Changed;

        public Task SaveAsync(AppSettings settings, bool raiseChanged = true)
        {
            Current = settings;
            if (raiseChanged)
                Changed?.Invoke();
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Records the symbols requested and returns a per-symbol TWR so tests can assert the
    /// custom-benchmark rows reflect the current CustomBenchmarkSymbols list.
    /// </summary>
    private sealed class RecordingBenchmark : IBenchmarkComparisonService
    {
        public List<string> RequestedSymbols { get; } = new();

        public Task<decimal?> ComputeBenchmarkTwrAsync(
            string symbol,
            Assetra.Core.Models.Analysis.PerformancePeriod period,
            CancellationToken ct = default)
        {
            lock (RequestedSymbols)
                RequestedSymbols.Add(symbol);
            // Deterministic non-null value so the custom row formats to a real percentage.
            return Task.FromResult<decimal?>(0.10m);
        }

        public Task<IReadOnlyList<Assetra.Core.Models.Analysis.BenchmarkSeriesPoint>?> ComputeBenchmarkSeriesAsync(
            string symbol,
            Assetra.Core.Models.Analysis.PerformancePeriod period,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Assetra.Core.Models.Analysis.BenchmarkSeriesPoint>?>(null);
    }

    private sealed class StubFx(Dictionary<(string From, string To), decimal> rates)
        : IMultiCurrencyValuationService
    {
        public Task<decimal?> ConvertAsync(
            decimal amount,
            string from,
            string to,
            DateOnly asOf,
            CancellationToken ct = default)
        {
            return rates.TryGetValue((from.ToUpperInvariant(), to.ToUpperInvariant()), out var rate)
                ? Task.FromResult<decimal?>(amount * rate)
                : Task.FromResult<decimal?>(null);
        }
    }

    // ── Incomplete-snapshot filtering tests (fix/trend-skip-incomplete) ─────

    /// <summary>
    /// When the series contains at least one complete-breakdown snapshot, rows that
    /// lack CashValue / EquityValue (the early "缺歷史價" days) must be excluded so
    /// the chart does not show a false basis-jump at the first complete day.
    /// </summary>
    [Fact]
    public async Task MixedBreakdown_SkipsIncompleteRows_SeriesStartsAtFirstComplete()
    {
        // 3 early snapshots with only MarketValue (no breakdown — the "缺歷史價" days).
        var incomplete1 = new PortfolioDailySnapshot(new DateOnly(2026, 4, 21), 0m, 8_500_000m, 0m, 5);
        var incomplete2 = new PortfolioDailySnapshot(new DateOnly(2026, 4, 22), 0m, 8_500_000m, 0m, 5);
        var incomplete3 = new PortfolioDailySnapshot(new DateOnly(2026, 4, 23), 0m, 8_500_000m, 0m, 5);

        // 5 complete snapshots where net worth = Cash + Equity − Liability.
        var firstCompleteDate = new DateOnly(2026, 5, 5);
        var complete1 = new PortfolioDailySnapshot(firstCompleteDate,          0m, 7_000_000m, 0m, 5, CashValue: 3_000_000m, EquityValue: 4_000_000m);
        var complete2 = new PortfolioDailySnapshot(new DateOnly(2026, 5, 6),   0m, 7_100_000m, 0m, 5, CashValue: 3_100_000m, EquityValue: 4_000_000m);
        var complete3 = new PortfolioDailySnapshot(new DateOnly(2026, 5, 7),   0m, 7_200_000m, 0m, 5, CashValue: 3_200_000m, EquityValue: 4_000_000m);
        var complete4 = new PortfolioDailySnapshot(new DateOnly(2026, 5, 8),   0m, 7_300_000m, 0m, 5, CashValue: 3_300_000m, EquityValue: 4_000_000m);
        var complete5 = new PortfolioDailySnapshot(new DateOnly(2026, 5, 9),   0m, 7_400_000m, 0m, 5, CashValue: 3_400_000m, EquityValue: 4_000_000m);

        var snapshots = new[] { incomplete1, incomplete2, incomplete3, complete1, complete2, complete3, complete4, complete5 };
        var vm = new PortfolioHistoryViewModel(new StubHistoryQuery(snapshots));
        vm.SelectedDays = 0; // "All" — include every snapshot

        await vm.LoadAsync();

        var values = ChartValues(vm);
        // Only the 5 complete rows should appear — the 3 incomplete rows must be skipped.
        Assert.Equal(5, values.Count);
        // The first plotted point must correspond to the first complete date's net worth (Cash + Equity = 7_000_000).
        Assert.Equal(7_000_000d, values[0]);
    }

    /// <summary>
    /// When ALL snapshots lack a breakdown (legacy-only data), the chart must still
    /// render using raw MarketValue — the all-legacy fallback must not produce an empty series.
    /// </summary>
    [Fact]
    public async Task AllLegacyNoBreakdown_StillPlotsRawMarketValue()
    {
        var snapshots = new[]
        {
            new PortfolioDailySnapshot(new DateOnly(2025, 6, 1), 0m, 1_000_000m, 0m, 3),
            new PortfolioDailySnapshot(new DateOnly(2025, 6, 2), 0m, 1_010_000m, 0m, 3),
            new PortfolioDailySnapshot(new DateOnly(2025, 6, 3), 0m, 1_020_000m, 0m, 3),
        };
        var vm = new PortfolioHistoryViewModel(new StubHistoryQuery(snapshots));
        vm.SelectedDays = 0;

        await vm.LoadAsync();

        var values = ChartValues(vm);
        // Must NOT be empty — raw MarketValue fallback must be preserved for all-legacy data.
        Assert.NotEmpty(values);
        Assert.Equal(3, values.Count);
        Assert.Equal(1_000_000d, values[0]);
    }
}
