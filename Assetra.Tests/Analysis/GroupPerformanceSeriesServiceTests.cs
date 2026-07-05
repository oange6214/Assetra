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
    public async Task UtcTradeCrossingLocalMidnight_AlignsHoldingAndFlowDates_NoSpuriousCrash()
    {
        // WHY: 交易時間以 UTC 存（如台灣午夜 00:00 = 前一日 16:00Z）。持倉日期原本用
        // DateOnly.FromDateTime（取 UTC 日）、現金流用 PerformancePeriod.ToPeriodDate（轉本地日）
        // → 兩者差一天：買進的現金流比持倉晚一天被扣 → segReturn≈-1 → 整條線暴跌 -100%
        // （使用者實例：柏翰）。修法：持倉也走 ToPeriodDate，與現金流同一套日期。
        // 全平價（10）→ 買進持有真實報酬應為 0%，絕不能出現 -100% 崩線。
        // 註：此錯位只在本地時區非 UTC（如 UTC+8）時重現；UTC 機器上兩者相等，測試仍會通過。
        var localMidnight = new DateTime(2026, 5, 2, 0, 0, 0, DateTimeKind.Local);
        var buy = new Trade(Guid.NewGuid(), "AAA", "TWSE", "AAA", TradeType.Buy,
            localMidnight.ToUniversalTime(), 10m, 100, null, null, PortfolioGroupId: GroupId);

        var flat = new (DateOnly, decimal)[]
        {
            (new DateOnly(2026, 5, 1), 10m),
            (new DateOnly(2026, 5, 2), 10m),
            (new DateOnly(2026, 5, 3), 10m),
            (new DateOnly(2026, 5, 5), 10m),
        };
        var repo = new Mock<ITradeRepository>();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new[] { buy });
        var svc = new GroupPerformanceSeriesService(
            repo.Object, new FakeHistory(flat), new TimeWeightedReturnCalculator());

        var series = await svc.ComputeGroupSeriesAsync(GroupId, Window);

        Assert.NotNull(series);
        // 全平價 → 每點累積 TWR 應 ≈ 0；絕不能出現 -100% 崩線（錯位 bug 的指紋）。
        Assert.All(series!, p => Assert.True(p.PercentFromStart > -0.5m,
            $"date {p.Date:yyyy-MM-dd} TWR={p.PercentFromStart} 疑似日期錯位崩線"));
        Assert.True(Math.Abs(series![^1].PercentFromStart) < 0.001m,
            $"平價買進持有 TWR 應≈0，實得 {series[^1].PercentFromStart}");
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
