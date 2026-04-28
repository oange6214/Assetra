using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.Data.Sqlite;
using Assetra.Core.Models;
using Assetra.Infrastructure.Persistence;
using Xunit;

namespace Assetra.Tests.Infrastructure;

public sealed class FindOrCreateRepositoryTests : IDisposable
{
    private readonly string _dbPath;
    public FindOrCreateRepositoryTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"stockra-test-{Guid.NewGuid():N}.db");
    }
    public void Dispose() { try { File.Delete(_dbPath); } catch { } }

    [Fact]
    public async Task FindOrCreateAccountAsync_NewName_InsertsAndReturnsNewGuid()
    {
        var repo = new AssetSqliteRepository(_dbPath);
        var id = await repo.FindOrCreateAccountAsync("國泰世華主帳戶", "TWD");

        Assert.NotEqual(Guid.Empty, id);
        var item = await repo.GetByIdAsync(id);
        Assert.NotNull(item);
        Assert.Equal("國泰世華主帳戶", item!.Name);
        Assert.Equal("TWD", item.Currency);
        Assert.Equal(FinancialType.Asset, item.Type);
        Assert.True(item.IsActive);
    }

    [Fact]
    public async Task FindOrCreateAccountAsync_ExistingName_ReturnsExistingGuid()
    {
        var repo = new AssetSqliteRepository(_dbPath);
        var first = await repo.FindOrCreateAccountAsync("富邦銀行", "TWD");
        var second = await repo.FindOrCreateAccountAsync("富邦銀行", "TWD");

        Assert.Equal(first, second);
        var all = await repo.GetItemsByTypeAsync(FinancialType.Asset);
        Assert.Single(all.Where(a => a.Name == "富邦銀行"));
    }

    [Fact]
    public async Task FindOrCreateAccountAsync_SameNameDifferentCurrency_CreatesTwoDistinctEntries()
    {
        var repo = new AssetSqliteRepository(_dbPath);
        var twd = await repo.FindOrCreateAccountAsync("國泰", "TWD");
        var usd = await repo.FindOrCreateAccountAsync("國泰", "USD");

        Assert.NotEqual(twd, usd);
    }

    [Fact]
    public async Task FindOrCreatePortfolioEntryAsync_NewSymbol_Inserts()
    {
        var repo = new PortfolioSqliteRepository(_dbPath);
        var id = await repo.FindOrCreatePortfolioEntryAsync("2330", "TW", "TSMC", AssetType.Stock);

        Assert.NotEqual(Guid.Empty, id);
        var all = await repo.GetEntriesAsync();
        var match = all.SingleOrDefault(e => e.Symbol == "2330" && e.Exchange == "TW");
        Assert.NotNull(match);
        Assert.True(match!.IsActive);
    }

    [Fact]
    public async Task FindOrCreatePortfolioEntryAsync_ExistingSymbol_ReturnsSameGuid()
    {
        var repo = new PortfolioSqliteRepository(_dbPath);
        var a = await repo.FindOrCreatePortfolioEntryAsync("2330", "TW", "TSMC", AssetType.Stock);
        var b = await repo.FindOrCreatePortfolioEntryAsync("2330", "TW", null, AssetType.Stock);

        Assert.Equal(a, b);
    }

    [Fact]
    public async Task FindOrCreatePortfolioEntryAsync_PersistsExplicitCurrency()
    {
        var repo = new PortfolioSqliteRepository(_dbPath);
        var id = await repo.FindOrCreatePortfolioEntryAsync("AAPL", "NASDAQ", "Apple", AssetType.Stock, currency: "USD");

        var entry = (await repo.GetEntriesAsync()).Single(e => e.Id == id);
        Assert.Equal("USD", entry.Currency);
    }

    [Fact]
    public async Task FindOrCreatePortfolioEntryAsync_NullCurrency_DefaultsToTwd()
    {
        var repo = new PortfolioSqliteRepository(_dbPath);
        var id = await repo.FindOrCreatePortfolioEntryAsync("2330", "TWSE", "TSMC", AssetType.Stock);

        var entry = (await repo.GetEntriesAsync()).Single(e => e.Id == id);
        Assert.Equal("TWD", entry.Currency);
    }

    [Fact]
    public async Task FindOrCreatePortfolioEntryAsync_SameSymbolDifferentExchange_CreatesTwo()
    {
        var repo = new PortfolioSqliteRepository(_dbPath);
        var tw = await repo.FindOrCreatePortfolioEntryAsync("2330", "TW", "TSMC.TW", AssetType.Stock);
        var hk = await repo.FindOrCreatePortfolioEntryAsync("2330", "HK", "TSMC.HK", AssetType.Stock);

        Assert.NotEqual(tw, hk);
    }

    [Fact]
    public async Task AssetRepo_HasTradeReferencesAsync_ReturnsZero_WhenNoTrades()
    {
        var repo = new AssetSqliteRepository(_dbPath);
        var id = await repo.FindOrCreateAccountAsync("Empty", "TWD");
        Assert.Equal(0, await repo.HasTradeReferencesAsync(id));
    }

    [Fact]
    public async Task PortfolioRepo_HasTradeReferencesAsync_ReturnsZero_WhenNoTrades()
    {
        var repo = new PortfolioSqliteRepository(_dbPath);
        var id = await repo.FindOrCreatePortfolioEntryAsync("2330", "TW", "TSMC", AssetType.Stock);
        Assert.Equal(0, await repo.HasTradeReferencesAsync(id));
    }
}
