using System.IO;
using Microsoft.Data.Sqlite;
using Assetra.Infrastructure.Persistence;
using Xunit;

namespace Assetra.Tests.Infrastructure;

/// <summary>
/// Regression tests for TradeSqliteRepository.Initialize() — migration ordering.
///
/// Wave 7 (2026-04-19) dropped the legacy cash_account / liability_account tables.
/// An unguarded Wave 6 backfill in Initialize() was still querying liability_account
/// directly, throwing "no such table" on both fresh installs AND on restart after
/// Wave 7 migration had completed.
/// </summary>
public class TradeSqliteRepositoryInitializeTests : IDisposable
{
    private readonly string _dbPath;

    public TradeSqliteRepositoryInitializeTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"trade_init_test_{Guid.NewGuid():N}.db");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }

    [Fact]
    public void Initialize_FreshDatabase_DoesNotThrow()
    {
        // On a fresh database, liability_account has never existed. The Wave 6
        // backfill must be guarded so it silently skips when the legacy table
        // is absent.
        var ex = Record.Exception(() => new TradeSqliteRepository(_dbPath));
        Assert.Null(ex);
    }

    [Fact]
    public void Initialize_AfterAssetRepositoryDropsLegacyTables_DoesNotThrow()
    {
        // Simulate the real app startup order: AssetSqliteRepository runs first,
        // performs Wave 7 migration (drops liability_account / cash_account on
        // legacy DBs, or leaves a fresh DB without them). Then TradeSqliteRepository
        // initializes — it must not assume liability_account still exists.
        _ = new AssetSqliteRepository(_dbPath);

        var ex = Record.Exception(() => new TradeSqliteRepository(_dbPath));
        Assert.Null(ex);
    }

    [Fact]
    public void Initialize_LegacyLiabilityAccountTablePresent_BackfillRunsSuccessfully()
    {
        // Pre-Wave-7 scenario: an existing install still has liability_account
        // present (because AssetSqliteRepository has not yet run). The backfill
        // should execute without error and link matching LoanBorrow trades.
        using (var conn = new SqliteConnection($"Data Source={_dbPath}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE liability_account (
                    id           TEXT PRIMARY KEY,
                    name         TEXT NOT NULL,
                    currency     TEXT,
                    created_date TEXT
                );
                INSERT INTO liability_account (id, name, currency, created_date)
                VALUES ('liab-1', '台新A 7y', 'TWD', '2026-01-01');
                """;
            cmd.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();

        var ex = Record.Exception(() => new TradeSqliteRepository(_dbPath));
        Assert.Null(ex);
    }
}
