using System.IO;
using Assetra.Core.Models;
using Assetra.Core.Models.Sync;
using Assetra.Infrastructure.Persistence;
using Assetra.Infrastructure.Sync;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Assetra.Tests.Infrastructure;

/// <summary>
/// MultiCurrency-Reporting P4.5b — verifies that <see cref="Trade.RealizedMarketPnl"/>
/// + <see cref="Trade.RealizedFxPnl"/> survive:
/// <list type="bullet">
///   <item>Local persistence round-trip via <see cref="TradeSqliteRepository"/></item>
///   <item>Sync envelope round-trip via <see cref="TradeSyncMapper"/></item>
/// </list>
/// </summary>
public sealed class TradeRealizedPnlBreakdownPersistenceTests : IDisposable
{
    private readonly string _dbPath;

    public TradeRealizedPnlBreakdownPersistenceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"trade_pnlbreakdown_{Guid.NewGuid():N}.db");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }

    [Fact]
    public async Task RepoRoundTrip_PreservesBreakdownFields()
    {
        var repo = new TradeSqliteRepository(_dbPath);
        var trade = new Trade(
            Id: Guid.NewGuid(),
            Symbol: "AAPL",
            Exchange: "NASDAQ",
            Name: "Apple",
            Type: TradeType.Sell,
            TradeDate: DateTime.UtcNow,
            Price: 110m,
            Quantity: 100,
            RealizedPnl: 52_000m,
            RealizedPnlPct: 17.33m,
            InstrumentCurrency: "USD",
            RealizedMarketPnl: 32_000m,
            RealizedFxPnl: 20_000m);

        await repo.AddAsync(trade);
        var fetched = await repo.GetByIdAsync(trade.Id);

        Assert.NotNull(fetched);
        Assert.Equal(32_000m, fetched!.RealizedMarketPnl);
        Assert.Equal(20_000m, fetched.RealizedFxPnl);
    }

    [Fact]
    public async Task RepoRoundTrip_NullBreakdown_StaysNull()
    {
        var repo = new TradeSqliteRepository(_dbPath);
        var trade = new Trade(
            Id: Guid.NewGuid(),
            Symbol: "2330",
            Exchange: "TWSE",
            Name: "TSMC",
            Type: TradeType.Sell,
            TradeDate: DateTime.UtcNow,
            Price: 600m,
            Quantity: 1_000,
            RealizedPnl: 50_000m,
            RealizedPnlPct: 9.09m,
            InstrumentCurrency: "TWD"); // no breakdown explicitly set → null

        await repo.AddAsync(trade);
        var fetched = await repo.GetByIdAsync(trade.Id);

        Assert.NotNull(fetched);
        Assert.Null(fetched!.RealizedMarketPnl);
        Assert.Null(fetched.RealizedFxPnl);
    }

    [Fact]
    public void SyncMapper_RoundTrip_PreservesBreakdownFields()
    {
        var original = new Trade(
            Id: Guid.NewGuid(),
            Symbol: "AAPL",
            Exchange: "NASDAQ",
            Name: "Apple",
            Type: TradeType.Sell,
            TradeDate: DateTime.UtcNow,
            Price: 110m,
            Quantity: 100,
            RealizedPnl: 52_000m,
            RealizedPnlPct: 17.33m,
            InstrumentCurrency: "USD",
            RealizedMarketPnl: 32_000m,
            RealizedFxPnl: 20_000m);
        var version = new EntityVersion(1, DateTimeOffset.UtcNow, "deviceA");

        var envelope = TradeSyncMapper.ToEnvelope(original, version, isDeleted: false);
        var decoded = TradeSyncMapper.FromPayload(envelope);

        Assert.Equal(32_000m, decoded.RealizedMarketPnl);
        Assert.Equal(20_000m, decoded.RealizedFxPnl);
    }

    [Fact]
    public void SyncMapper_RoundTrip_NullBreakdown_StaysNull()
    {
        var original = new Trade(
            Id: Guid.NewGuid(),
            Symbol: "2330",
            Exchange: "TWSE",
            Name: "TSMC",
            Type: TradeType.Sell,
            TradeDate: DateTime.UtcNow,
            Price: 600m,
            Quantity: 1_000,
            RealizedPnl: 50_000m,
            RealizedPnlPct: 9.09m);
        var version = new EntityVersion(1, DateTimeOffset.UtcNow, "deviceA");

        var envelope = TradeSyncMapper.ToEnvelope(original, version, isDeleted: false);
        var decoded = TradeSyncMapper.FromPayload(envelope);

        Assert.Null(decoded.RealizedMarketPnl);
        Assert.Null(decoded.RealizedFxPnl);
    }

    [Fact]
    public async Task RepoUpdate_PersistsBreakdownChanges()
    {
        var repo = new TradeSqliteRepository(_dbPath);
        var original = new Trade(
            Id: Guid.NewGuid(),
            Symbol: "AAPL",
            Exchange: "NASDAQ",
            Name: "Apple",
            Type: TradeType.Sell,
            TradeDate: DateTime.UtcNow,
            Price: 110m,
            Quantity: 100,
            RealizedPnl: 52_000m,
            RealizedPnlPct: 17.33m);
        await repo.AddAsync(original);

        // Backfill breakdown via Update (e.g. user retroactively populated FX history).
        var updated = original with { RealizedMarketPnl = 32_000m, RealizedFxPnl = 20_000m };
        await repo.UpdateAsync(updated);
        var fetched = await repo.GetByIdAsync(original.Id);

        Assert.NotNull(fetched);
        Assert.Equal(32_000m, fetched!.RealizedMarketPnl);
        Assert.Equal(20_000m, fetched.RealizedFxPnl);
    }
}
