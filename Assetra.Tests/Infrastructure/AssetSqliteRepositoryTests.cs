using System.IO;
using Assetra.Core.Models;
using Assetra.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
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
        Assert.Contains(groups, g => g.Name == "銀行類" && g.IsSystem);
        Assert.Contains(groups, g => g.Name == "手邊現金" && g.IsSystem);
        Assert.Contains(groups, g => g.Name == "證券交割款" && g.IsSystem);
        Assert.Contains(groups, g => g.Name == "電子支付" && g.IsSystem);
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

    // ── CreateOrRevive：同名同幣別「已刪除/已封存」帳戶不可阻擋重建（蝦皮 crash repro）─────────

    [Fact]
    public async Task CreateOrRevive_WhenSoftDeletedSameNameCurrencyExists_RevivesAsActiveAccount()
    {
        // 重現「蝦皮」當機：先前被軟刪除（is_deleted=1）的同名同幣別帳戶，因 (name,currency)
        // 唯一索引把墓碑列也算進去，導致裸 INSERT 撞 UNIQUE → SQLite Error 19。
        // CreateOrRevive 應改為「就地復活」成全新啟用帳戶，而非丟例外。
        var repo = new AssetSqliteRepository(_dbPath);
        var original = new AssetItem(
            Guid.NewGuid(), "蝦皮", FinancialType.Asset, null, "TWD",
            new DateOnly(2026, 1, 1), Subtype: "電子支付");
        await repo.AddItemAsync(original);
        await repo.DeleteItemAsync(original.Id);   // soft delete → tombstone

        var fresh = new AssetItem(
            Guid.NewGuid(), "蝦皮", FinancialType.Asset, null, "TWD",
            new DateOnly(2026, 5, 30), Subtype: "電子支付");
        var outcome = await repo.CreateOrReviveAccountAsync(fresh);

        Assert.Equal(AccountCreateStatus.Revived, outcome.Status);
        Assert.Equal(original.Id, outcome.Id);     // 沿用既有列，而非 fresh.Id

        var revived = await repo.GetByIdAsync(outcome.Id);   // GetById 會過濾 is_deleted=0
        Assert.NotNull(revived);                              // 不再是墓碑、查得到
        Assert.True(revived!.IsActive);
        Assert.Equal("蝦皮", revived.Name);

        var items = await repo.GetItemsAsync();
        Assert.Single(items, i => i.Name == "蝦皮" && i.Currency == "TWD");
    }

    [Fact]
    public async Task CreateOrRevive_WhenNoExisting_CreatesNew()
    {
        var repo = new AssetSqliteRepository(_dbPath);
        var item = new AssetItem(
            Guid.NewGuid(), "中信", FinancialType.Asset, null, "TWD", new DateOnly(2026, 1, 1));

        var outcome = await repo.CreateOrReviveAccountAsync(item);

        Assert.Equal(AccountCreateStatus.Created, outcome.Status);
        Assert.Equal(item.Id, outcome.Id);
        Assert.NotNull(await repo.GetByIdAsync(item.Id));
    }

    [Fact]
    public async Task CreateOrRevive_WhenActiveDuplicateExists_ReportsDuplicateActiveWithoutSecondRow()
    {
        var repo = new AssetSqliteRepository(_dbPath);
        var live = new AssetItem(
            Guid.NewGuid(), "玉山", FinancialType.Asset, null, "TWD", new DateOnly(2026, 1, 1));
        await repo.AddItemAsync(live);

        var dup = new AssetItem(
            Guid.NewGuid(), "玉山", FinancialType.Asset, null, "TWD", new DateOnly(2026, 5, 30));
        var outcome = await repo.CreateOrReviveAccountAsync(dup);

        Assert.Equal(AccountCreateStatus.DuplicateActive, outcome.Status);
        Assert.Equal(live.Id, outcome.Id);          // 指向既有啟用帳戶
        var items = await repo.GetItemsAsync();
        Assert.Single(items, i => i.Name == "玉山");  // 沒有產生第二列
    }

    // ── FindOrCreate：找到墓碑/封存列必須「真的復活」，而非回傳隱形帳戶 Id ────────────────

    [Fact]
    public async Task FindOrCreate_WhenSoftDeletedMatchExists_RevivesAndReturnsSameId()
    {
        // 修正前的隱患：FindOrCreate 找到墓碑會回傳其 Id 卻沒把 is_deleted 設回 0，
        // 導致轉帳/存入記到一筆指向「已刪除、列表看不到」帳戶的交易。
        var repo = new AssetSqliteRepository(_dbPath);
        var original = new AssetItem(
            Guid.NewGuid(), "悠遊付", FinancialType.Asset, null, "TWD", new DateOnly(2026, 1, 1));
        await repo.AddItemAsync(original);
        await repo.DeleteItemAsync(original.Id);   // soft delete → tombstone

        var id = await repo.FindOrCreateAccountAsync("悠遊付", "TWD");

        Assert.Equal(original.Id, id);             // 沿用墓碑列
        var revived = await repo.GetByIdAsync(id); // GetById 過濾 is_deleted=0
        Assert.NotNull(revived);                   // 已不再隱形
        Assert.True(revived!.IsActive);
    }

    [Fact]
    public async Task FindOrCreate_WhenArchivedMatchExists_RevivesToActive()
    {
        var repo = new AssetSqliteRepository(_dbPath);
        var original = new AssetItem(
            Guid.NewGuid(), "一卡通", FinancialType.Asset, null, "TWD", new DateOnly(2026, 1, 1));
        await repo.AddItemAsync(original);
        await repo.ArchiveItemAsync(original.Id);  // is_active = 0

        var id = await repo.FindOrCreateAccountAsync("一卡通", "TWD");

        Assert.Equal(original.Id, id);
        var revived = await repo.GetByIdAsync(id);
        Assert.NotNull(revived);
        Assert.True(revived!.IsActive);            // 已取消封存
    }

    [Fact]
    public async Task FindOrCreate_WhenLiveMatchExists_ReturnsSameIdWithoutDuplicating()
    {
        var repo = new AssetSqliteRepository(_dbPath);
        var original = new AssetItem(
            Guid.NewGuid(), "街口", FinancialType.Asset, null, "TWD", new DateOnly(2026, 1, 1));
        await repo.AddItemAsync(original);

        var id = await repo.FindOrCreateAccountAsync("街口", "TWD");

        Assert.Equal(original.Id, id);
        var items = await repo.GetItemsAsync();
        Assert.Single(items, i => i.Name == "街口");
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
    public async Task Items_UpdateSubtypeAndGroup_RoundTripsThroughGetByType()
    {
        var repo = new AssetSqliteRepository(_dbPath);
        var bankGroupId = new Guid("11111111-1111-1111-1111-111111111101");
        var item = new AssetItem(Guid.NewGuid(), "富邦", FinancialType.Asset,
            null, "TWD", new DateOnly(2026, 4, 20));
        await repo.AddItemAsync(item);

        await repo.UpdateItemAsync(item with
        {
            Subtype = "銀行活存",
            GroupId = bankGroupId,
        });

        var assets = await repo.GetItemsByTypeAsync(FinancialType.Asset);
        var found = Assert.Single(assets.Where(i => i.Id == item.Id));
        Assert.Equal("銀行活存", found.Subtype);
        Assert.Equal(bankGroupId, found.GroupId);
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

    [Fact]
    public async Task Events_GetLatestValuations_EmptyInput_ReturnsEmptyDict()
    {
        var repo = new AssetSqliteRepository(_dbPath);
        var result = await repo.GetLatestValuationsAsync(Array.Empty<Guid>());
        Assert.Empty(result);
    }

    [Fact]
    public async Task Events_GetLatestValuations_BatchReturnsLatestPerAsset()
    {
        var repo = new AssetSqliteRepository(_dbPath);
        var a = new AssetItem(Guid.NewGuid(), "資產A", FinancialType.Asset,
            null, "TWD", DateOnly.FromDateTime(DateTime.Today));
        var b = new AssetItem(Guid.NewGuid(), "資產B", FinancialType.Asset,
            null, "TWD", DateOnly.FromDateTime(DateTime.Today));
        var c = new AssetItem(Guid.NewGuid(), "資產C", FinancialType.Asset,
            null, "TWD", DateOnly.FromDateTime(DateTime.Today));
        await repo.AddItemAsync(a);
        await repo.AddItemAsync(b);
        await repo.AddItemAsync(c);

        // a has two valuations — newer wins
        await repo.AddEventAsync(new AssetEvent(Guid.NewGuid(), a.Id, AssetEventType.Valuation,
            DateTime.UtcNow.AddDays(-10), 100m, null, null, null, DateTime.UtcNow));
        await repo.AddEventAsync(new AssetEvent(Guid.NewGuid(), a.Id, AssetEventType.Valuation,
            DateTime.UtcNow.AddDays(-1), 200m, null, null, null, DateTime.UtcNow));

        // b has one valuation
        await repo.AddEventAsync(new AssetEvent(Guid.NewGuid(), b.Id, AssetEventType.Valuation,
            DateTime.UtcNow.AddDays(-5), 555m, null, null, null, DateTime.UtcNow));

        // c has only a Transaction event — should be excluded from result
        await repo.AddEventAsync(new AssetEvent(Guid.NewGuid(), c.Id, AssetEventType.Transaction,
            DateTime.UtcNow, 999m, null, null, null, DateTime.UtcNow));

        var result = await repo.GetLatestValuationsAsync(new[] { a.Id, b.Id, c.Id });

        Assert.Equal(2, result.Count);
        Assert.Equal(200m, result[a.Id].Amount);
        Assert.Equal(555m, result[b.Id].Amount);
        Assert.False(result.ContainsKey(c.Id));
    }

    [Fact]
    public async Task Events_GetLatestValuations_DistinctsDuplicateInputs()
    {
        var repo = new AssetSqliteRepository(_dbPath);
        var a = new AssetItem(Guid.NewGuid(), "資產", FinancialType.Asset,
            null, "TWD", DateOnly.FromDateTime(DateTime.Today));
        await repo.AddItemAsync(a);
        await repo.AddEventAsync(new AssetEvent(Guid.NewGuid(), a.Id, AssetEventType.Valuation,
            DateTime.UtcNow, 42m, null, null, null, DateTime.UtcNow));

        var result = await repo.GetLatestValuationsAsync(new[] { a.Id, a.Id, a.Id });

        Assert.Single(result);
        Assert.Equal(42m, result[a.Id].Amount);
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
    public async Task Items_PaymentMethodType_RoundTripsAndSeparatesFromLiability()
    {
        var repo = new AssetSqliteRepository(_dbPath);
        var card = new AssetItem(
            Guid.NewGuid(),
            "台新 @GoGo",
            FinancialType.PaymentMethod,
            null,
            "TWD",
            DateOnly.FromDateTime(DateTime.Today),
            LiabilitySubtype: LiabilitySubtype.CreditCard,
            BillingDay: 15,
            IssuerName: "台新銀行");

        await repo.AddItemAsync(card);

        var found = await repo.GetByIdAsync(card.Id);
        Assert.NotNull(found);
        Assert.Equal(FinancialType.PaymentMethod, found!.Type);

        var paymentMethods = await repo.GetItemsByTypeAsync(FinancialType.PaymentMethod);
        Assert.Contains(paymentMethods, a => a.Id == card.Id);

        var liabilities = await repo.GetItemsByTypeAsync(FinancialType.Liability);
        Assert.DoesNotContain(liabilities, a => a.Id == card.Id);
    }

    [Fact]
    public async Task Items_PaymentMethodDefaults_RoundTrip()
    {
        var repo = new AssetSqliteRepository(_dbPath);
        var bank = Guid.NewGuid();
        var category = Guid.NewGuid();
        var card = new AssetItem(
            Guid.NewGuid(),
            "台新 @GoGo",
            FinancialType.PaymentMethod,
            null,
            "TWD",
            DateOnly.FromDateTime(DateTime.Today),
            LiabilitySubtype: LiabilitySubtype.CreditCard,
            BillingDay: 15,
            IssuerName: "台新銀行",
            DefaultCashAccountId: bank,
            DefaultCategoryId: category);

        await repo.AddItemAsync(card);
        var found = await repo.GetByIdAsync(card.Id);

        Assert.NotNull(found);
        Assert.Equal(bank, found!.DefaultCashAccountId);
        Assert.Equal(category, found.DefaultCategoryId);
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
        Assert.Equal(1, groups.Count(g => g.Name == "銀行類" && g.IsSystem));
    }
}
