using Assetra.Application.Analysis;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Assetra.Core.Models.Analysis;
using Moq;
using Xunit;

namespace Assetra.Tests.Analysis;

public sealed class GroupPerformanceSeriesServiceTests
{
    private static readonly Guid GroupId = Guid.NewGuid();

    private static readonly PerformancePeriod Window =
        new(new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 5));

    // 收盤 10 → 11 → 12.1 ⇒ 每段 +10%，累積 0% / +10% / +21%（皆為乾淨 decimal，避免重複小數誤差）。
    private static readonly IReadOnlyList<(DateOnly Date, decimal Close)> AaaPrices =
    [
        (new DateOnly(2026, 5, 1), 10m),
        (new DateOnly(2026, 5, 3), 11m),
        (new DateOnly(2026, 5, 5), 12.1m),
    ];

    private static Trade Move(TradeType type, int qty, DateOnly date) =>
        new(Guid.NewGuid(), "AAA", "TWSE", "AAA", type,
            date.ToDateTime(TimeOnly.MinValue), 10m, qty, null, null, PortfolioGroupId: GroupId);

    private static GroupPerformanceSeriesService Build(params Trade[] trades)
    {
        var repo = new Mock<ITradeRepository>();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(trades);
        return new GroupPerformanceSeriesService(
            repo.Object, new FakeHistory(AaaPrices), new TimeWeightedReturnCalculator());
    }

    [Fact]
    public async Task PrePeriodBuy_HeldThrough_IsPurePriceReturn()
    {
        // 期初前買 100 股、整段持有、無期間內現金流 → 純價格報酬：0% / +10% / +21%。
        var svc = Build(Move(TradeType.Buy, 100, new DateOnly(2026, 4, 30)));

        var series = await svc.ComputeGroupSeriesAsync(GroupId, Window);

        Assert.NotNull(series);
        Assert.Equal(3, series!.Count);
        Assert.Equal(0m, series[0].PercentFromStart);
        Assert.Equal(0.10m, series[1].PercentFromStart);
        Assert.Equal(0.21m, series[2].PercentFromStart);
    }

    [Fact]
    public async Task SignedQuantity_BuyMinusSell_ReconstructsHoldings()
    {
        // 期初前買 200、賣 100 → 持倉 100；序列與「持有 100」相同（驗 signed-qty 重建）。
        var svc = Build(
            Move(TradeType.Buy, 200, new DateOnly(2026, 4, 28)),
            Move(TradeType.Sell, 100, new DateOnly(2026, 4, 30)));

        var series = await svc.ComputeGroupSeriesAsync(GroupId, Window);

        Assert.NotNull(series);
        Assert.Equal(0.21m, series![^1].PercentFromStart);
    }

    [Fact]
    public async Task NoTradesInGroup_ReturnsNull()
    {
        // 不同群組的交易不算進來。
        var other = new Trade(Guid.NewGuid(), "AAA", "TWSE", "AAA", TradeType.Buy,
            new DateTime(2026, 4, 30), 10m, 100, null, null, PortfolioGroupId: Guid.NewGuid());
        var svc = Build(other);

        Assert.Null(await svc.ComputeGroupSeriesAsync(GroupId, Window));
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
