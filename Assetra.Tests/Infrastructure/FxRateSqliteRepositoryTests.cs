using System.IO;
using Assetra.Core.Models;
using Assetra.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Assetra.Tests.Infrastructure;

public class FxRateSqliteRepositoryTests : IDisposable
{
    private readonly string _dbPath;

    public FxRateSqliteRepositoryTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"fxrate_test_{Guid.NewGuid():N}.db");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }

    [Fact]
    public async Task Upsert_Then_Get_RoundTrips()
    {
        var repo = new FxRateSqliteRepository(_dbPath);
        await repo.UpsertAsync(new FxRate("USD", "TWD", 32.5m, new DateOnly(2026, 4, 28)));

        var hit = await repo.GetAsync("USD", "TWD", new DateOnly(2026, 4, 28));
        Assert.NotNull(hit);
        Assert.Equal("USD", hit!.From);
        Assert.Equal("TWD", hit.To);
        Assert.Equal(32.5m, hit.Rate);
    }

    [Fact]
    public async Task Upsert_OnDuplicateKey_OverwritesRate()
    {
        var repo = new FxRateSqliteRepository(_dbPath);
        var date = new DateOnly(2026, 4, 28);
        await repo.UpsertAsync(new FxRate("USD", "TWD", 32m, date));
        await repo.UpsertAsync(new FxRate("USD", "TWD", 33m, date));

        var hit = await repo.GetAsync("USD", "TWD", date);
        Assert.Equal(33m, hit!.Rate);
    }

    [Fact]
    public async Task GetAsync_NoExactDate_ReturnsNearestPriorRate()
    {
        var repo = new FxRateSqliteRepository(_dbPath);
        await repo.UpsertAsync(new FxRate("USD", "TWD", 32m, new DateOnly(2026, 4, 20)));
        await repo.UpsertAsync(new FxRate("USD", "TWD", 32.5m, new DateOnly(2026, 4, 25)));

        var hit = await repo.GetAsync("USD", "TWD", new DateOnly(2026, 4, 28));
        Assert.NotNull(hit);
        Assert.Equal(new DateOnly(2026, 4, 25), hit!.AsOfDate);
        Assert.Equal(32.5m, hit.Rate);
    }

    [Fact]
    public async Task GetRangeAsync_ReturnsAscendingByDate()
    {
        var repo = new FxRateSqliteRepository(_dbPath);
        await repo.UpsertManyAsync(new[]
        {
            new FxRate("USD", "TWD", 32m, new DateOnly(2026, 4, 20)),
            new FxRate("USD", "TWD", 33m, new DateOnly(2026, 4, 22)),
            new FxRate("USD", "TWD", 31m, new DateOnly(2026, 4, 18)),
        });

        var rs = await repo.GetRangeAsync("USD", "TWD", new DateOnly(2026, 4, 19), new DateOnly(2026, 4, 23));
        Assert.Equal(2, rs.Count);
        Assert.Equal(new DateOnly(2026, 4, 20), rs[0].AsOfDate);
        Assert.Equal(new DateOnly(2026, 4, 22), rs[1].AsOfDate);
    }
}
