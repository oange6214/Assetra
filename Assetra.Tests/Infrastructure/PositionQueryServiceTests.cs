using Moq;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Assetra.Infrastructure;
using Xunit;

namespace Assetra.Tests.Infrastructure;

public sealed class PositionQueryServiceTests
{
    private static readonly Guid EntryId = Guid.Parse("11111111-1111-1111-1111-000000000001");

    private static PositionQueryService BuildSvc(params Trade[] trades)
    {
        var repo = new Mock<ITradeRepository>();
        repo.Setup(r => r.GetAllAsync()).ReturnsAsync(trades);
        return new PositionQueryService(repo.Object);
    }

    private static Trade Buy(DateTime date, decimal price, int qty, decimal commission = 0m) =>
        new(Guid.NewGuid(), "2330", "TW", "TSMC",
            TradeType.Buy, date, price, qty,
            RealizedPnl: null, RealizedPnlPct: null,
            Commission: commission, PortfolioEntryId: EntryId);

    private static Trade Sell(DateTime date, decimal price, int qty, decimal commission = 0m) =>
        new(Guid.NewGuid(), "2330", "TW", "TSMC",
            TradeType.Sell, date, price, qty,
            RealizedPnl: null, RealizedPnlPct: null,
            Commission: commission, PortfolioEntryId: EntryId);

    private static Trade StockDiv(DateTime date, int qty) =>
        new(Guid.NewGuid(), "2330", "TW", "TSMC",
            TradeType.StockDividend, date, 0m, qty,
            RealizedPnl: null, RealizedPnlPct: null,
            PortfolioEntryId: EntryId);

    [Fact]
    public async Task GetPositionAsync_ReturnsNull_WhenNoTrades()
    {
        var svc = BuildSvc();
        var snap = await svc.GetPositionAsync(EntryId);
        Assert.Null(snap);
    }

    [Fact]
    public async Task GetPositionAsync_SingleBuy_ReturnsExpectedSnapshot()
    {
        var svc = BuildSvc(Buy(new DateTime(2026, 1, 1), 10m, 100));

        var snap = await svc.GetPositionAsync(EntryId);

        Assert.NotNull(snap);
        Assert.Equal(100m, snap!.Quantity);
        Assert.Equal(1000m, snap.TotalCost);
        Assert.Equal(10m, snap.AverageCost);
        Assert.Equal(0m, snap.RealizedPnl);
        Assert.Equal(new DateOnly(2026, 1, 1), snap.FirstBuyDate);
    }

    [Fact]
    public async Task GetPositionAsync_TwoBuys_WeightedAverage()
    {
        var svc = BuildSvc(
            Buy(new DateTime(2026, 1, 1), 10m, 100),
            Buy(new DateTime(2026, 1, 2), 20m, 50));

        var snap = await svc.GetPositionAsync(EntryId);

        Assert.NotNull(snap);
        Assert.Equal(150m, snap!.Quantity);
        Assert.Equal(2000m, snap.TotalCost);
        Assert.Equal(2000m / 150m, snap.AverageCost);
        Assert.Equal(new DateOnly(2026, 1, 1), snap.FirstBuyDate);
    }

    [Fact]
    public async Task GetPositionAsync_BuyThenSell_ReducesProportionally()
    {
        var svc = BuildSvc(
            Buy(new DateTime(2026, 1, 1), 10m, 100),  // cost 1000
            Sell(new DateTime(2026, 1, 3), 15m, 50)); // proceeds 750, cogs 500

        var snap = await svc.GetPositionAsync(EntryId);

        Assert.NotNull(snap);
        Assert.Equal(50m, snap!.Quantity);
        Assert.Equal(500m, snap.TotalCost);
        Assert.Equal(10m, snap.AverageCost);
        Assert.Equal(250m, snap.RealizedPnl);
    }

    [Fact]
    public async Task GetPositionAsync_SellAll_ReturnsZeroQty_KeepsRealizedPnl()
    {
        var svc = BuildSvc(
            Buy(new DateTime(2026, 1, 1), 10m, 100),
            Sell(new DateTime(2026, 1, 3), 15m, 100));

        var snap = await svc.GetPositionAsync(EntryId);

        Assert.NotNull(snap);
        Assert.Equal(0m, snap!.Quantity);
        Assert.Equal(0m, snap.TotalCost);
        Assert.Equal(0m, snap.AverageCost);   // guard: avoid div-by-zero
        Assert.Equal(500m, snap.RealizedPnl);
    }

    [Fact]
    public async Task GetPositionAsync_StockDividend_DilutesAverageCost()
    {
        var svc = BuildSvc(
            Buy(new DateTime(2026, 1, 1), 10m, 100),
            StockDiv(new DateTime(2026, 6, 1), 20));

        var snap = await svc.GetPositionAsync(EntryId);

        Assert.NotNull(snap);
        Assert.Equal(120m, snap!.Quantity);
        Assert.Equal(1000m, snap.TotalCost); // unchanged
        Assert.Equal(1000m / 120m, snap.AverageCost); // diluted
        Assert.Equal(0m, snap.RealizedPnl);
    }

    [Fact]
    public async Task GetPositionAsync_BuyCommission_IncreasesTotalCost()
    {
        var svc = BuildSvc(Buy(new DateTime(2026, 1, 1), 10m, 100, commission: 5m));

        var snap = await svc.GetPositionAsync(EntryId);

        Assert.NotNull(snap);
        Assert.Equal(1005m, snap!.TotalCost);
        Assert.Equal(10.05m, snap.AverageCost);
    }

    [Fact]
    public async Task GetPositionAsync_SellCommission_ReducesProceeds_NotCogs()
    {
        // Buy 100 @ $10 no commission → cost 1000, avg 10
        // Sell 50 @ $15 commission $3 → proceeds 15*50 - 3 = 747, cogs 1000*50/100 = 500
        //   realized = 747 - 500 = 247
        var svc = BuildSvc(
            Buy(new DateTime(2026, 1, 1), 10m, 100, commission: 0m),
            Sell(new DateTime(2026, 1, 3), 15m, 50, commission: 3m));

        var snap = await svc.GetPositionAsync(EntryId);

        Assert.NotNull(snap);
        Assert.Equal(50m, snap!.Quantity);
        Assert.Equal(500m, snap.TotalCost);
        Assert.Equal(247m, snap.RealizedPnl);
    }

    [Fact]
    public async Task GetAllPositionSnapshotsAsync_GroupsByEntryId()
    {
        var entryA = Guid.Parse("22222222-2222-2222-2222-00000000000A");
        var entryB = Guid.Parse("22222222-2222-2222-2222-00000000000B");

        Trade BuyFor(Guid entry, DateTime date, decimal price, int qty) =>
            new(Guid.NewGuid(), "SYM", "TW", "N",
                TradeType.Buy, date, price, qty,
                RealizedPnl: null, RealizedPnlPct: null,
                PortfolioEntryId: entry);

        var repo = new Mock<ITradeRepository>();
        repo.Setup(r => r.GetAllAsync()).ReturnsAsync(new Trade[]
        {
            BuyFor(entryA, new DateTime(2026, 1, 1), 10m, 100),
            BuyFor(entryB, new DateTime(2026, 1, 2),  5m,  20),
        });
        var svc = new PositionQueryService(repo.Object);

        var all = await svc.GetAllPositionSnapshotsAsync();

        Assert.Equal(2, all.Count);
        Assert.Equal(100m, all[entryA].Quantity);
        Assert.Equal(20m, all[entryB].Quantity);
        Assert.Equal(10m, all[entryA].AverageCost);
        Assert.Equal(5m, all[entryB].AverageCost);
    }

    [Fact]
    public async Task GetAllPositionSnapshotsAsync_SkipsTradesWithoutEntry()
    {
        // Cash-only trades (Deposit, Income) have PortfolioEntryId = null; must be skipped.
        var cashTrade = new Trade(
            Guid.NewGuid(), "", "", "薪資",
            TradeType.Income, new DateTime(2026, 1, 1), 0m, 1,
            RealizedPnl: null, RealizedPnlPct: null,
            CashAmount: 50000m, PortfolioEntryId: null);

        var repo = new Mock<ITradeRepository>();
        repo.Setup(r => r.GetAllAsync()).ReturnsAsync(new[] { cashTrade });
        var svc = new PositionQueryService(repo.Object);

        var all = await svc.GetAllPositionSnapshotsAsync();

        Assert.Empty(all);
    }

    [Fact]
    public async Task ComputeRealizedPnlAsync_UsesCurrentAvgCost()
    {
        // History: Buy 100 @ $10 → avg $10
        // Hypothetical Sell: 50 @ $15 with $3 fees
        //   proceeds = 15*50 - 3 = 747
        //   cogs = 1000 * 50/100 = 500
        //   realized = 247
        var svc = BuildSvc(Buy(new DateTime(2026, 1, 1), 10m, 100));

        var pnl = await svc.ComputeRealizedPnlAsync(
            EntryId, new DateTime(2026, 1, 3), 15m, 50m, 3m);

        Assert.Equal(247m, pnl);
    }

    [Fact]
    public async Task ComputeRealizedPnlAsync_ReturnsZero_WhenNoHistory()
    {
        var svc = BuildSvc();
        var pnl = await svc.ComputeRealizedPnlAsync(
            EntryId, DateTime.UtcNow, 10m, 50m, 0m);
        Assert.Equal(0m, pnl);
    }

    [Fact]
    public async Task GetPositionAsync_OverSell_ProducesNegativeQuantity()
    {
        // Data-integrity error scenario — should not throw, returns resulting state.
        // Buy 50 @ $10, then Sell 100 → totalQty goes negative; no COGS deducted since
        // the qty guard disables the COGS path when qty-before-sell is ≤ 0.
        var svc = BuildSvc(
            Buy(new DateTime(2026, 1, 1), 10m, 50),
            Sell(new DateTime(2026, 1, 3), 15m, 100));

        var snap = await svc.GetPositionAsync(EntryId);

        Assert.NotNull(snap);
        Assert.True(snap!.Quantity < 0m, $"Expected negative quantity, got {snap.Quantity}");
    }

    [Fact]
    public async Task GetPositionAsync_FirstBuyDate_IsEarliestBuyDate()
    {
        // Dividend before first buy — FirstBuyDate should still be first *Buy*'s date,
        // not the dividend date. The projection only sets firstBuy in the Buy branch.
        var svc = BuildSvc(
            StockDiv(new DateTime(2023, 12, 1), 5),
            Buy(new DateTime(2024, 1, 15), 10m, 100),
            Buy(new DateTime(2024, 3, 1), 12m, 50));

        var snap = await svc.GetPositionAsync(EntryId);

        Assert.NotNull(snap);
        Assert.Equal(new DateOnly(2024, 1, 15), snap!.FirstBuyDate);
    }
}
