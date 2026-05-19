using System.IO;
using Assetra.Core.Models;
using Assetra.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Assetra.Tests.Infrastructure;

/// <summary>
/// MultiCurrency-Reporting P4.1 — verifies the SQLite-backed FX history
/// store. Each test uses a fresh per-file temp DB so writes don't bleed.
/// </summary>
public sealed class FxRateHistorySqliteRepositoryTests : IDisposable
{
    private readonly string _dbPath;
    private readonly FxRateHistorySqliteRepository _repo;

    public FxRateHistorySqliteRepositoryTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"fx_hist_{Guid.NewGuid():N}.db");
        _repo = new FxRateHistorySqliteRepository(_dbPath);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }

    private static FxRateHistoryEntry Entry(DateOnly d, string b, string q, decimal r, string src = "test")
        => new(d, b, q, r, src, DateTimeOffset.UtcNow);

    [Fact]
    public async Task GetAsync_ExactMatch_ReturnsEntry()
    {
        var d = new DateOnly(2025, 12, 31);
        await _repo.UpsertRangeAsync(new[] { Entry(d, "USD", "TWD", 31.5m) });

        var result = await _repo.GetAsync(d, "USD", "TWD");

        Assert.NotNull(result);
        Assert.Equal(31.5m, result!.Rate);
        Assert.Equal("USD", result.BaseCurrency);
        Assert.Equal("TWD", result.QuoteCurrency);
    }

    [Fact]
    public async Task GetAsync_NoMatch_ReturnsNull()
    {
        var result = await _repo.GetAsync(new DateOnly(2025, 12, 31), "USD", "TWD");
        Assert.Null(result);
    }

    [Fact]
    public async Task RoundTrip_TWDtoUSDtoTWD_IsIdentity()
    {
        var d = new DateOnly(2025, 12, 31);
        // Two reciprocal rows — historical data sometimes only stores one direction;
        // here we store both for clean verification of the math at the test layer.
        await _repo.UpsertRangeAsync(new[]
        {
            Entry(d, "USD", "TWD", 31.5m),
            Entry(d, "TWD", "USD", 1m / 31.5m),
        });

        var usdToTwd = await _repo.GetAsync(d, "USD", "TWD");
        var twdToUsd = await _repo.GetAsync(d, "TWD", "USD");
        Assert.NotNull(usdToTwd);
        Assert.NotNull(twdToUsd);

        var roundTrip = usdToTwd!.Rate * twdToUsd!.Rate;
        Assert.InRange(roundTrip, 0.999m, 1.001m);
    }

    [Fact]
    public async Task GetNearestAsync_MissingExact_FallsBackToPriorDate()
    {
        var monday = new DateOnly(2025, 12, 29);
        await _repo.UpsertRangeAsync(new[] { Entry(monday, "USD", "TWD", 31.2m) });

        // Sunday — no row exists for this date, but Monday is within 7 days.
        var sunday = new DateOnly(2025, 12, 28);
        var result = await _repo.GetNearestAsync(sunday, "USD", "TWD");

        // Note: GetNearestAsync looks at "<= date", so Sunday request can't see Monday.
        // Use a Wednesday request instead to test the backward walk.
        Assert.Null(result); // Sunday is BEFORE Monday — correctly returns null.

        var wednesday = new DateOnly(2025, 12, 31);
        var fallback = await _repo.GetNearestAsync(wednesday, "USD", "TWD");
        Assert.NotNull(fallback);
        Assert.Equal(31.2m, fallback!.Rate);
        Assert.Equal(monday, fallback.Date);
    }

    [Fact]
    public async Task GetNearestAsync_OutsideLookbackWindow_ReturnsNull()
    {
        await _repo.UpsertRangeAsync(new[]
        {
            Entry(new DateOnly(2025, 1, 1), "USD", "TWD", 30m),
        });

        var farFuture = new DateOnly(2025, 12, 31);
        var result = await _repo.GetNearestAsync(farFuture, "USD", "TWD", lookbackDays: 7);

        Assert.Null(result);
    }

    [Fact]
    public async Task UpsertRangeAsync_IsIdempotent_NoDuplicates()
    {
        var d = new DateOnly(2025, 12, 31);
        var first = Entry(d, "USD", "TWD", 31.5m);
        var second = Entry(d, "USD", "TWD", 31.7m); // same PK, different rate

        await _repo.UpsertRangeAsync(new[] { first });
        await _repo.UpsertRangeAsync(new[] { second });

        // Second upsert overwrote first; range query should see exactly one row.
        var range = await _repo.GetRangeAsync("USD", "TWD", d, d);
        var row = Assert.Single(range);
        Assert.Equal(31.7m, row.Rate); // latest value wins
    }

    [Fact]
    public async Task GetRangeAsync_OrdersByDateAscending()
    {
        await _repo.UpsertRangeAsync(new[]
        {
            Entry(new DateOnly(2025, 12, 31), "USD", "TWD", 31.5m),
            Entry(new DateOnly(2025, 12, 29), "USD", "TWD", 31.2m),
            Entry(new DateOnly(2025, 12, 30), "USD", "TWD", 31.3m),
        });

        var range = await _repo.GetRangeAsync(
            "USD", "TWD", new DateOnly(2025, 12, 1), new DateOnly(2025, 12, 31));

        Assert.Equal(3, range.Count);
        Assert.Equal(new DateOnly(2025, 12, 29), range[0].Date);
        Assert.Equal(new DateOnly(2025, 12, 30), range[1].Date);
        Assert.Equal(new DateOnly(2025, 12, 31), range[2].Date);
    }

    [Fact]
    public async Task CurrencyCodesAreNormalizedToUpperCase()
    {
        var d = new DateOnly(2025, 12, 31);
        await _repo.UpsertRangeAsync(new[] { Entry(d, "usd", "twd", 31.5m) });

        var withLower = await _repo.GetAsync(d, "usd", "twd");
        var withUpper = await _repo.GetAsync(d, "USD", "TWD");

        Assert.NotNull(withLower);
        Assert.NotNull(withUpper);
        Assert.Equal(withLower!.Rate, withUpper!.Rate);
        Assert.Equal("USD", withLower.BaseCurrency);
        Assert.Equal("TWD", withLower.QuoteCurrency);
    }
}
