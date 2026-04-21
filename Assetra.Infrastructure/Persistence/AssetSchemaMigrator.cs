using Microsoft.Data.Sqlite;

namespace Assetra.Infrastructure.Persistence;

internal static class AssetSchemaMigrator
{
    // Fixed UUIDs for system groups — deterministic so INSERT OR IGNORE is idempotent
    private static readonly Guid GrpBankAccount = new("11111111-1111-1111-1111-111111111101");
    private static readonly Guid GrpRealEstate = new("11111111-1111-1111-1111-111111111102");
    private static readonly Guid GrpVehicle = new("11111111-1111-1111-1111-111111111103");
    private static readonly Guid GrpBankLoan = new("11111111-1111-1111-1111-111111111201");
    private static readonly Guid GrpCreditCard = new("11111111-1111-1111-1111-111111111202");

    private static readonly HashSet<string> AssetAllowedColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        "is_active", "updated_at",
        "loan_annual_rate", "loan_term_months", "loan_start_date", "loan_handling_fee",
    };

    private static readonly HashSet<string> AssetAllowedTypeDefs = new(StringComparer.OrdinalIgnoreCase)
    {
        "INTEGER NOT NULL DEFAULT 1",
        "TEXT NOT NULL DEFAULT ''",
        "REAL",
        "INTEGER",
        "TEXT",
    };

    public static void EnsureInitialized(string connectionString)
    {
        using var conn = new SqliteConnection(connectionString);
        conn.Open();
        using var tx = conn.BeginTransaction();
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;

            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS asset_group (
                    id           TEXT PRIMARY KEY,
                    name         TEXT NOT NULL,
                    asset_type   TEXT NOT NULL,
                    icon         TEXT,
                    sort_order   INTEGER NOT NULL DEFAULT 0,
                    is_system    INTEGER NOT NULL DEFAULT 0,
                    created_date TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS asset (
                    id           TEXT PRIMARY KEY,
                    name         TEXT NOT NULL,
                    asset_type   TEXT NOT NULL,
                    group_id     TEXT REFERENCES asset_group(id),
                    currency     TEXT NOT NULL DEFAULT 'TWD',
                    created_date TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS asset_event (
                    id               TEXT PRIMARY KEY,
                    asset_id         TEXT NOT NULL REFERENCES asset(id),
                    event_type       TEXT NOT NULL,
                    event_date       TEXT NOT NULL,
                    amount           REAL,
                    quantity         REAL,
                    note             TEXT,
                    cash_account_id  TEXT,
                    created_at       TEXT NOT NULL DEFAULT ''
                );

                CREATE INDEX IF NOT EXISTS idx_asset_type     ON asset (asset_type);
                CREATE INDEX IF NOT EXISTS idx_asset_event_id ON asset_event (asset_id);
                """;
            cmd.ExecuteNonQuery();

            SeedSystemGroups(cmd);
            AddAssetMetadataColumns(conn, tx);
            EnsureAssetIndexes(conn, tx);
            MigrateLegacyCashAccounts(cmd, conn, tx);
            MigrateLegacyLiabilityAccounts(cmd, conn, tx);

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    private static void SeedSystemGroups(SqliteCommand cmd)
    {
        cmd.CommandText = """
            INSERT OR IGNORE INTO asset_group (id, name, asset_type, icon, sort_order, is_system, created_date) VALUES
                ($g1, '銀行帳戶', 'Asset',     '🏦', 0, 1, '2026-01-01'),
                ($g2, '不動產',   'Asset',     '🏠', 1, 1, '2026-01-01'),
                ($g3, '交通工具', 'Asset',     '🚗', 2, 1, '2026-01-01'),
                ($g4, '銀行貸款', 'Liability', '🏦', 0, 1, '2026-01-01'),
                ($g5, '信用卡',   'Liability', '💳', 1, 1, '2026-01-01');
            """;
        cmd.Parameters.AddWithValue("$g1", GrpBankAccount.ToString());
        cmd.Parameters.AddWithValue("$g2", GrpRealEstate.ToString());
        cmd.Parameters.AddWithValue("$g3", GrpVehicle.ToString());
        cmd.Parameters.AddWithValue("$g4", GrpBankLoan.ToString());
        cmd.Parameters.AddWithValue("$g5", GrpCreditCard.ToString());
        cmd.ExecuteNonQuery();
        cmd.Parameters.Clear();
    }

    private static void AddAssetMetadataColumns(SqliteConnection conn, SqliteTransaction tx)
    {
        SqliteSchemaHelper.MigrateAddColumn(conn, tx, "asset",
            "is_active", "INTEGER NOT NULL DEFAULT 1",
            AssetAllowedColumns, AssetAllowedTypeDefs);
        SqliteSchemaHelper.MigrateAddColumn(conn, tx, "asset",
            "updated_at", "TEXT NOT NULL DEFAULT ''",
            AssetAllowedColumns, AssetAllowedTypeDefs);
        SqliteSchemaHelper.MigrateAddColumn(conn, tx, "asset",
            "loan_annual_rate", "REAL", AssetAllowedColumns, AssetAllowedTypeDefs);
        SqliteSchemaHelper.MigrateAddColumn(conn, tx, "asset",
            "loan_term_months", "INTEGER", AssetAllowedColumns, AssetAllowedTypeDefs);
        SqliteSchemaHelper.MigrateAddColumn(conn, tx, "asset",
            "loan_start_date", "TEXT", AssetAllowedColumns, AssetAllowedTypeDefs);
        SqliteSchemaHelper.MigrateAddColumn(conn, tx, "asset",
            "loan_handling_fee", "REAL", AssetAllowedColumns, AssetAllowedTypeDefs);
    }

    private static void EnsureAssetIndexes(SqliteConnection conn, SqliteTransaction tx)
    {
        using var idx = conn.CreateCommand();
        idx.Transaction = tx;
        idx.CommandText = """
            CREATE UNIQUE INDEX IF NOT EXISTS idx_asset_name_currency_asset
            ON asset(name, currency) WHERE asset_type = 'Asset'
            """;
        idx.ExecuteNonQuery();
    }

    private static void MigrateLegacyCashAccounts(
        SqliteCommand cmd, SqliteConnection conn, SqliteTransaction tx)
    {
        if (!TableExists(conn, tx, "cash_account"))
            return;

        cmd.CommandText = $"""
            INSERT OR IGNORE INTO asset (id, name, asset_type, group_id, currency, created_date)
            SELECT id, name, 'Asset', '{GrpBankAccount}', COALESCE(currency, 'TWD'), created_date
            FROM cash_account;
            """;
        cmd.ExecuteNonQuery();

        cmd.CommandText = "DROP TABLE IF EXISTS cash_account;";
        cmd.ExecuteNonQuery();
    }

    private static void MigrateLegacyLiabilityAccounts(
        SqliteCommand cmd, SqliteConnection conn, SqliteTransaction tx)
    {
        if (!TableExists(conn, tx, "liability_account"))
            return;

        cmd.CommandText = $"""
            INSERT OR IGNORE INTO asset (id, name, asset_type, group_id, currency, created_date)
            SELECT id, name, 'Liability', '{GrpBankLoan}', COALESCE(currency, 'TWD'),
                   COALESCE(created_date, date('now'))
            FROM liability_account;
            """;
        cmd.ExecuteNonQuery();

        cmd.CommandText = "DROP TABLE IF EXISTS liability_account;";
        cmd.ExecuteNonQuery();
    }

    private static bool TableExists(SqliteConnection conn, SqliteTransaction tx, string table)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$t;";
        cmd.Parameters.AddWithValue("$t", table);
        return (long)(cmd.ExecuteScalar() ?? 0L) > 0;
    }
}
