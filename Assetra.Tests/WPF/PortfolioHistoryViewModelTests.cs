using Assetra.Application.Portfolio.Contracts;
using Assetra.Core.Interfaces;
using Assetra.Core.Interfaces.Analysis;
using Assetra.Core.Models;
using Assetra.Core.Models.Analysis;
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
    public async Task ShortPeriod_WithSparseSnapshots_StillRendersBenchmarkComparison()
    {
        // WHY: benchmark 走每日 K 線、不該被「投組快照密度」綁住。投組快照很疏（5 天視窗內只剩 1 筆）時，
        // 選 5D 仍要畫出 benchmark 線（修「選 5D 圖空白」）。兩筆快照相隔 10 天 → 5D 視窗只含最後一筆。
        var latest = DateOnly.FromDateTime(DateTime.Today);
        var snapshots = new[]
        {
            new PortfolioDailySnapshot(latest.AddDays(-10), 0m, 1_000m, 0m, 1),
            new PortfolioDailySnapshot(latest, 0m, 1_100m, 0m, 1),
        };
        var vm = new PortfolioHistoryViewModel(
            new StubHistoryQuery(snapshots),
            settings: new FakeSettings(new AppSettings(BaseCurrency: "TWD", ComparisonItems: ["0050.TW"])),
            benchmark: new StubBenchmark());

        await vm.LoadAsync();
        vm.ChangePeriodCommand.Execute("5");
        await Task.Yield();

        Assert.Equal("5", vm.ActivePeriod);
        // 舊 gate（filtered.Count >= 2）會讓近幾天快照疏的情況整張圖空白；新邏輯用「選的視窗」畫 benchmark。
        Assert.NotEmpty(vm.CompareSeries);
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

    // 舊「同期對標報酬率」固定表的相關測試（HidesBenchmarkRow / ComputesDepositBaseline /
    // CustomBenchmarksChange / UnrelatedSettingsChange）已移除 — 該子系統已刪、比較改為 ComparisonItems。

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

    private sealed class StubBenchmark : IBenchmarkComparisonService
    {
        public Task<decimal?> ComputeBenchmarkTwrAsync(
            string symbol, PerformancePeriod period, CancellationToken ct = default)
            => Task.FromResult<decimal?>(0.05m);

        public Task<IReadOnlyList<BenchmarkSeriesPoint>?> ComputeBenchmarkSeriesAsync(
            string symbol, PerformancePeriod period, IntradayRange? intraday = null, CancellationToken ct = default)
        {
            IReadOnlyList<BenchmarkSeriesPoint> pts =
            [
                new BenchmarkSeriesPoint(period.Start.ToDateTime(TimeOnly.MinValue), 0m, 100m),
                new BenchmarkSeriesPoint(period.End.ToDateTime(TimeOnly.MinValue), 0.05m, 105m),
            ];
            return Task.FromResult<IReadOnlyList<BenchmarkSeriesPoint>?>(pts);
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

    // ── 「加了卻畫不出」的比較項目 → 灰色 ⚠ chip（不再靜默消失）─────────────────
    //
    // WHY: 使用者把自建群組（如「柏翰」）加入比較，但該群組買賣不成對／缺價格
    // → ComputeGroupSeriesAsync 回 null。舊行為：token 靜默存進設定卻無 chip、無線，
    // 看起來「根本加不進去」。新行為：補一顆「可移除、hover 看原因」的灰色 ⚠ chip，
    // 並讓 HasComparisonItems=true（隱藏「點＋比較」空提示）。若有人把失敗項改回靜默
    // 略過（原本的 `continue`），這條測試就會紅——正是要鎖住的意圖。
    [Fact]
    public async Task GroupComparison_WhenSeriesCannotCompute_ShowsRemovableUnavailableChip()
    {
        var token = PortfolioHistoryViewModel.GroupTokenPrefix + Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.Today);
        var snapshots = new[]
        {
            new PortfolioDailySnapshot(today.AddDays(-20), 0m, 1_000m, 0m, 1),
            new PortfolioDailySnapshot(today, 0m, 1_100m, 0m, 1),
        };
        var vm = new PortfolioHistoryViewModel(
            new StubHistoryQuery(snapshots),
            settings: new FakeSettings(new AppSettings(BaseCurrency: "TWD", ComparisonItems: [token])),
            groupPerformance: new StubGroupPerf(series: null)); // 群組序列算不出來
        vm.SelectedDays = 0; // "All" → 日線區間（非盤中）→ 真的會去算群組序列

        await vm.LoadAsync();

        var chip = Assert.Single(vm.ComparisonLegend);
        Assert.True(chip.IsUnavailable);
        Assert.Equal(token, chip.RemoveSymbol);                          // 仍可移除（✕）
        Assert.False(string.IsNullOrWhiteSpace(chip.UnavailableReason)); // hover 有原因
        Assert.True(vm.HasComparisonItems);                             // 不再顯示空提示
        Assert.Empty(vm.CompareSeries);                                 // 沒有可畫的線
    }

    // 移除最後一個比較項目後，上一輪的「無法顯示」灰色 chip 不可殘留（幽靈 chip）。
    // BuildComparisonLinesAsync 只在有項目時才跑，故 refresh 時必須主動清空 _comparisonUnavailable，
    // 否則清單已空、卻還掛著一顆 ⚠ chip、HasComparisonItems 還是 true。
    [Fact]
    public async Task RemovingLastComparisonItem_ClearsUnavailableChip_NoGhost()
    {
        var token = PortfolioHistoryViewModel.GroupTokenPrefix + Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.Today);
        var snapshots = new[]
        {
            new PortfolioDailySnapshot(today.AddDays(-20), 0m, 1_000m, 0m, 1),
            new PortfolioDailySnapshot(today, 0m, 1_100m, 0m, 1),
        };
        var settings = new FakeSettings(new AppSettings(BaseCurrency: "TWD", ComparisonItems: [token]));
        var vm = new PortfolioHistoryViewModel(
            new StubHistoryQuery(snapshots),
            settings: settings,
            groupPerformance: new StubGroupPerf(series: null));
        vm.SelectedDays = 0;

        await vm.LoadAsync();
        Assert.Single(vm.ComparisonLegend); // 先有一顆 ⚠ chip
        Assert.True(vm.HasComparisonItems);

        // 移除最後一個項目 → 再 refresh
        await settings.SaveAsync(settings.Current with { ComparisonItems = [] }, raiseChanged: false);
        await vm.LoadAsync();

        Assert.Empty(vm.ComparisonLegend);       // 幽靈 chip 已清
        Assert.False(vm.HasComparisonItems);     // 回到「點＋比較」空提示
    }

    private sealed class StubGroupPerf(IReadOnlyList<BenchmarkSeriesPoint>? series)
        : IGroupPerformanceSeriesService
    {
        public Task<IReadOnlyList<BenchmarkSeriesPoint>?> ComputeGroupSeriesAsync(
            Guid groupId, PerformancePeriod period, CancellationToken ct = default)
            => Task.FromResult(series);
    }
}
