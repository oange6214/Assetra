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
