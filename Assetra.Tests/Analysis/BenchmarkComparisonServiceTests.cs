using Assetra.Application.Analysis;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Assetra.Core.Models.Analysis;
using Xunit;

namespace Assetra.Tests.Analysis;

public sealed class BenchmarkComparisonServiceTests
{
    private static readonly PerformancePeriod Window =
        new(new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 5));

    [Fact]
    public async Task ComputeBenchmarkSeriesAsync_NormalizesEachDayFromStart()
    {
        // WHY: the overlay needs every benchmark to start at 0% so different price scales compare on
        // one chart. close 100 → 110 → 105 ⇒ 0% / +10% / +5% from the period start.
        var svc = new BenchmarkComparisonService(new FakeHistory(
        [
            (new DateOnly(2026, 5, 1), 100m),
            (new DateOnly(2026, 5, 3), 110m),
            (new DateOnly(2026, 5, 5), 105m),
        ]));

        var series = await svc.ComputeBenchmarkSeriesAsync("^TWII", Window);

        Assert.NotNull(series);
        Assert.Equal(3, series!.Count);
        Assert.Equal(0m, series[0].PercentFromStart);
        Assert.Equal(0.10m, series[1].PercentFromStart);
        Assert.Equal(0.05m, series[2].PercentFromStart);

        // The series endpoint must equal the standalone TWR (same definition, no drift).
        var twr = await svc.ComputeBenchmarkTwrAsync("^TWII", Window);
        Assert.Equal(twr, series[^1].PercentFromStart);
    }

    [Fact]
    public async Task ComputeBenchmarkSeriesAsync_OnlyOneInRangePoint_ReturnsNull()
    {
        // A single in-range close can't form a return → null (caller hides that benchmark line).
        var svc = new BenchmarkComparisonService(new FakeHistory(
        [
            (new DateOnly(2026, 5, 1), 100m),
            (new DateOnly(2026, 7, 1), 130m), // out of the window
        ]));

        Assert.Null(await svc.ComputeBenchmarkSeriesAsync("^TWII", Window));
    }

    [Fact]
    public async Task ComputeBenchmarkSeriesAsync_FirstCandleAfterStart_UsesPrePeriodBaselineAndPrependsStart()
    {
        // 標的在期初當天（05/05）無資料 → 用「期初前」最近一根（05/01=100）當基準，並補一個期初 0% 點，
        // 讓線從選定的期初日 05/05 起跑（而非該標的第一根 in-range 05/10）。修「00830 看起來缺五月資料」。
        var svc = new BenchmarkComparisonService(new FakeHistory(
        [
            (new DateOnly(2026, 5, 1), 100m),   // 期初前的基準
            (new DateOnly(2026, 5, 10), 110m),
            (new DateOnly(2026, 5, 15), 121m),
        ]));
        var window = new PerformancePeriod(new DateOnly(2026, 5, 5), new DateOnly(2026, 5, 15));

        var series = await svc.ComputeBenchmarkSeriesAsync("0050.TW", window);

        Assert.NotNull(series);
        Assert.Equal(3, series!.Count);
        Assert.Equal(new DateOnly(2026, 5, 5), series[0].Date);   // 補的期初點
        Assert.Equal(0m, series[0].PercentFromStart);
        Assert.Equal(100m, series[0].Value);                      // 基準 = 期初前 05/01 收盤
        Assert.Equal(0.10m, series[1].PercentFromStart);          // 110/100 − 1
        Assert.Equal(0.21m, series[2].PercentFromStart);          // 121/100 − 1
    }

    [Fact]
    public async Task ComputeBenchmarkSeriesAsync_RecentShortWindow_UsesLastNTradingDays()
    {
        // 近期短窗（如 5D）＝「最近 N 個交易日收盤」，不靠日曆對齊：即使視窗日期比資料新（快照比行情新）或
        // 撞週末，也畫得出 N 點折線而非 2 點斜線。修「1D/5D 變斜線」。7 根 ＋ 5 天視窗 → 取最後 5 根（05/08..05/14）。
        var svc = new BenchmarkComparisonService(new FakeHistory(
        [
            (new DateOnly(2026, 5, 6), 90m),
            (new DateOnly(2026, 5, 7), 95m),
            (new DateOnly(2026, 5, 8), 100m),   // ← 最後 5 根從這裡開始
            (new DateOnly(2026, 5, 9), 110m),
            (new DateOnly(2026, 5, 12), 105m),
            (new DateOnly(2026, 5, 13), 115m),
            (new DateOnly(2026, 5, 14), 121m),
        ]));
        // 視窗 5 天、且比最後一根（05/14）新 → 取最後 5 根交易日。
        var window = new PerformancePeriod(new DateOnly(2026, 5, 18), new DateOnly(2026, 5, 22));

        var series = await svc.ComputeBenchmarkSeriesAsync("0050.TW", window);

        Assert.NotNull(series);
        Assert.Equal(5, series!.Count);                          // 5 個交易日折線（非 2 點斜線）
        Assert.Equal(new DateOnly(2026, 5, 8), series[0].Date);  // 基準 = 最後 5 根的第一根
        Assert.Equal(0m, series[0].PercentFromStart);
        Assert.Equal(0.21m, series[^1].PercentFromStart);        // 121/100 − 1
    }

    private sealed class FakeHistory(IReadOnlyList<(DateOnly Date, decimal Close)> points)
        : IStockHistoryProvider
    {
        public Task<IReadOnlyList<OhlcvPoint>> GetHistoryAsync(
            string symbol, string exchange, ChartPeriod period, CancellationToken ct = default)
        {
            IReadOnlyList<OhlcvPoint> candles = points
                .Select(p => new OhlcvPoint(p.Date, p.Close, p.Close, p.Close, p.Close, 0L))
                .ToList();
            return Task.FromResult(candles);
        }
    }
}
