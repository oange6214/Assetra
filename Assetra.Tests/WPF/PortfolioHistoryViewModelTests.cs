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

    private sealed class StubBenchmark(decimal? result) : IBenchmarkComparisonService
    {
        public Task<decimal?> ComputeBenchmarkTwrAsync(
            string symbol,
            Assetra.Core.Models.Analysis.PerformancePeriod period,
            CancellationToken ct = default) =>
            Task.FromResult(result);
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

    private sealed class FakeSettings(string baseCurrency) : IAppSettingsService
    {
        public AppSettings Current { get; private set; } = new(BaseCurrency: baseCurrency);
        public event Action? Changed;

        public Task SaveAsync(AppSettings settings)
        {
            Current = settings;
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
}
