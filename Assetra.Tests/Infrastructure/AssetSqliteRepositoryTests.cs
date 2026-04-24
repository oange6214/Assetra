using System.IO;
using Microsoft.Data.Sqlite;
using Assetra.Core.Models;
using Assetra.Infrastructure.Persistence;
using Xunit;

namespace Assetra.Tests.Infrastructure;

public class AssetSqliteRepositoryTests : IDisposable
{
    private readonly string _dbPath;

    public AssetSqliteRepositoryTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"asset_test_{Guid.NewGuid():N}.db");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }

    // ── Groups ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Groups_AddAndGet_RoundTrips()
    {
        var repo = new AssetSqliteRepository(_dbPath);
        var group = new AssetGroup(Guid.NewGuid(), "測試群組", FinancialType.Asset,
            "🏦", 0, false, DateOnly.FromDateTime(DateTime.Today));

        await repo.AddGroupAsync(group);
        var groups = await repo.GetGroupsAsync();

        // System groups are seeded on init; also find the one we added
        var found = groups.FirstOrDefault(g => g.Id == group.Id);
        Assert.NotNull(found);
        Assert.Equal("測試群組", found.Name);
        Assert.Equal(FinancialType.Asset, found.Type);
        Assert.False(found.IsSystem);
    }

    [Fact]
    public async Task Groups_SystemGroupsSeededOnInit()
    {
        var repo = new AssetSqliteRepository(_dbPath);
        var groups = await repo.GetGroupsAsync();
        Assert.Contains(groups, g => g.Name == "銀行帳戶" && g.IsSystem);
        Assert.Contains(groups, g => g.Name == "銀行貸款" && g.IsSystem && g.Type == FinancialType.Liability);
    }

    [Fact]
    public async Task Groups_Update_PersistsChanges()
    {
        var repo = new AssetSqliteRepository(_dbPath);
        var group = new AssetGroup(Guid.NewGuid(), "原始名稱", FinancialType.Asset,
            null, 1, false, DateOnly.FromDateTime(DateTime.Today));
        await repo.AddGroupAsync(group);

        var updated = group with { Name = "更新名稱" };
        await repo.UpdateGroupAsync(updated);

        var groups = await repo.GetGroupsAsync();
        Assert.Equal("更新名稱", groups.First(g => g.Id == group.Id).Name);
    }

    [Fact]
    public async Task Groups_Delete_RemovesGroup()
    {
        var repo = new AssetSqliteRepository(_dbPath);
        var group = new AssetGroup(Guid.NewGuid(), "待刪群組", FinancialType.Asset,
            null, 0, false, DateOnly.FromDateTime(DateTime.Today));
        await repo.AddGroupAsync(group);
        await repo.DeleteGroupAsync(group.Id);

        var groups = await repo.GetGroupsAsync();
        Assert.DoesNotContain(groups, g => g.Id == group.Id);
    }

    // ── Items ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Items_AddAndGetByType_FiltersCorrectly()
    {
        var repo = new AssetSqliteRepository(_dbPath);
        var assetItem = new AssetItem(Guid.NewGuid(), "台新帳戶", FinancialType.Asset,
            null, "TWD", DateOnly.FromDateTime(DateTime.Today));
        var liabItem = new AssetItem(Guid.NewGuid(), "房貸", FinancialType.Liability,
            null, "TWD", DateOnly.FromDateTime(DateTime.Today));

        await repo.AddItemAsync(assetItem);
        await repo.AddItemAsync(liabItem);

        var assets = await repo.GetItemsByTypeAsync(FinancialType.Asset);
        var liabilities = await repo.GetItemsByTypeAsync(FinancialType.Liability);

        Assert.Contains(assets, i => i.Id == assetItem.Id);
        Assert.DoesNotContain(assets, i => i.Id == liabItem.Id);
        Assert.Contains(liabilities, i => i.Id == liabItem.Id);
    }

    [Fact]
    public async Task Items_GetById_ReturnsCorrectItem()
    {
        var repo = new AssetSqliteRepository(_dbPath);
        var item = new AssetItem(Guid.NewGuid(), "查詢測試", FinancialType.Asset,
            null, "USD", DateOnly.FromDateTime(DateTime.Today));
        await repo.AddItemAsync(item);

        var found = await repo.GetByIdAsync(item.Id);
        Assert.NotNull(found);
        Assert.Equal("USD", found.Currency);
    }

    [Fact]
    public async Task Items_Delete_RemovesItem()
    {
        var repo = new AssetSqliteRepository(_dbPath);
        var item = new AssetItem(Guid.NewGuid(), "待刪", FinancialType.Asset,
            null, "TWD", DateOnly.FromDateTime(DateTime.Today));
        await repo.AddItemAsync(item);
        await repo.DeleteItemAsync(item.Id);

        var found = await repo.GetByIdAsync(item.Id);
        Assert.Null(found);
    }

    // ── Events ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Events_GetLatestValuation_ReturnsNewest()
    {
        var repo = new AssetSqliteRepository(_dbPath);
        var item = new AssetItem(Guid.NewGuid(), "不動產", FinancialType.Asset,
            null, "TWD", DateOnly.FromDateTime(DateTime.Today));
        await repo.AddItemAsync(item);

        var older = new AssetEvent(Guid.NewGuid(), item.Id, AssetEventType.Valuation,
            DateTime.UtcNow.AddDays(-10), 1_500_000m, null, null, null, DateTime.UtcNow);
        var newer = new AssetEvent(Guid.NewGuid(), item.Id, AssetEventType.Valuation,
            DateTime.UtcNow.AddDays(-1), 1_600_000m, null, null, null, DateTime.UtcNow);

        await repo.AddEventAsync(older);
        await repo.AddEventAsync(newer);

        var latest = await repo.GetLatestValuationAsync(item.Id);
        Assert.NotNull(latest);
        Assert.Equal(1_600_000m, latest.Amount);
    }

    [Fact]
    public async Task Events_GetLatestValuation_IgnoresTransactionType()
    {
        var repo = new AssetSqliteRepository(_dbPath);
        var item = new AssetItem(Guid.NewGuid(), "現金帳戶", FinancialType.Asset,
            null, "TWD", DateOnly.FromDateTime(DateTime.Today));
        await repo.AddItemAsync(item);

        // Only a Transaction event — no Valuation
        var txEvent = new AssetEvent(Guid.NewGuid(), item.Id, AssetEventType.Transaction,
            DateTime.UtcNow, 50_000m, null, "存款", null, DateTime.UtcNow);
        await repo.AddEventAsync(txEvent);

        var latest = await repo.GetLatestValuationAsync(item.Id);
        Assert.Null(latest);
    }

    // ── Wave 7 Migration ─────────────────────────────────────────────────────

    [Fact]
    public async Task Wave7_MigratesCashAccounts_ToAsset()
    {
        // Arrange: pre-create legacy cash_account table with test data
        var cashId = Guid.NewGuid();
        using (var conn = new SqliteConnection($"Data Source={_dbPath}"))
        {
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE cash_account (
                    id           TEXT PRIMARY KEY,
                    name         TEXT NOT NULL,
                    balance      REAL NOT NULL DEFAULT 0,
                    created_date TEXT NOT NULL,
                    currency     TEXT NOT NULL DEFAULT 'TWD'
                );
                INSERT INTO cash_account (id, name, balance, created_date, currency)
                VALUES ($id, '台新 Richart', 0, '2025-01-01', 'TWD');
                """;
            cmd.Parameters.AddWithValue("$id", cashId.ToString());
            await cmd.ExecuteNonQueryAsync();
        }

        // Act: initialize repo (triggers Wave 7)
        var repo = new AssetSqliteRepository(_dbPath);

        // Assert: old row appears in asset table with correct type
        var items = await repo.GetItemsByTypeAsync(FinancialType.Asset);
        Assert.Contains(items, i => i.Id == cashId && i.Name == "台新 Richart");

        // Assert: old table is gone
        SqliteConnection.ClearAllPools();
        using (var conn = new SqliteConnection($"Data Source={_dbPath}"))
        {
            await conn.OpenAsync();
            using var check = conn.CreateCommand();
            check.CommandText =
                "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='cash_account'";
            var count = (long)(await check.ExecuteScalarAsync() ?? 0L);
            Assert.Equal(0L, count);
        }
    }

    [Fact]
    public async Task Wave7_MigratesLiabilityAccounts_ToLiability()
    {
        var liabId = Guid.NewGuid();
        using (var conn = new SqliteConnection($"Data Source={_dbPath}"))
        {
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE liability_account (
                    id              TEXT PRIMARY KEY,
                    name            TEXT NOT NULL,
                    balance         REAL NOT NULL DEFAULT 0,
                    created_date    TEXT NOT NULL,
                    currency        TEXT NOT NULL DEFAULT 'TWD'
                );
                INSERT INTO liability_account (id, name, balance, created_date, currency)
                VALUES ($id, '台新A 7y', 0, '2024-06-01', 'TWD');
                """;
            cmd.Parameters.AddWithValue("$id", liabId.ToString());
            await cmd.ExecuteNonQueryAsync();
        }

        var repo = new AssetSqliteRepository(_dbPath);

        var items = await repo.GetItemsByTypeAsync(FinancialType.Liability);
        Assert.Contains(items, i => i.Id == liabId && i.Name == "台新A 7y");
        Assert.Contains(items, i => i.Id == liabId && i.LiabilitySubtype == LiabilitySubtype.Loan);
    }

    [Fact]
    public async Task Items_CreditCardMetadata_RoundTrips()
    {
        var repo = new AssetSqliteRepository(_dbPath);
        var item = new AssetItem(
            Guid.NewGuid(),
            "國泰 Cube",
            FinancialType.Liability,
            null,
            "TWD",
            DateOnly.FromDateTime(DateTime.Today),
            LiabilitySubtype: LiabilitySubtype.CreditCard,
            BillingDay: 8,
            DueDay: 23,
            CreditLimit: 200_000m,
            IssuerName: "國泰世華");

        await repo.AddItemAsync(item);

        var found = await repo.GetByIdAsync(item.Id);
        Assert.NotNull(found);
        Assert.Equal(LiabilitySubtype.CreditCard, found.LiabilitySubtype);
        Assert.Equal(8, found.BillingDay);
        Assert.Equal(23, found.DueDay);
        Assert.Equal(200_000m, found.CreditLimit);
        Assert.Equal("國泰世華", found.IssuerName);
    }

    [Fact]
    public async Task Wave7_IsIdempotent_WhenRunTwice()
    {
        // First init
        _ = new AssetSqliteRepository(_dbPath);
        // Second init — should not throw
        var ex = Record.Exception(() => new AssetSqliteRepository(_dbPath));
        Assert.Null(ex);

        var repo = new AssetSqliteRepository(_dbPath);
        var groups = await repo.GetGroupsAsync();
        // System groups should appear exactly once each
        Assert.Equal(1, groups.Count(g => g.Name == "銀行帳戶" && g.IsSystem));
    }
}
