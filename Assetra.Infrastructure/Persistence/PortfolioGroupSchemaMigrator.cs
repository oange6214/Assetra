using Assetra.Core.Models;
using Microsoft.Data.Sqlite;

namespace Assetra.Infrastructure.Persistence;

internal static class PortfolioGroupSchemaMigrator
{
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
                CREATE TABLE IF NOT EXISTS portfolio_group (
                    id                       TEXT PRIMARY KEY,
                    name                     TEXT NOT NULL,
                    color_hex                TEXT,
                    description              TEXT,
                    icon_key                 TEXT,
                    sort_order               INTEGER NOT NULL DEFAULT 0,
                    default_cash_account_id  TEXT,
                    is_system                INTEGER NOT NULL DEFAULT 0,
                    created_at               TEXT NOT NULL DEFAULT '',
                    updated_at               TEXT NOT NULL DEFAULT ''
                );
                CREATE INDEX IF NOT EXISTS idx_portfolio_group_sort ON portfolio_group(sort_order);
                """;
            cmd.ExecuteNonQuery();

            // Seed the default group (idempotent — INSERT OR IGNORE on stable Guid).
            // All pre-existing trades / cash accounts that lack a group will be
            // assigned to this row by separate per-table backfill steps.
            using var seed = conn.CreateCommand();
            seed.Transaction = tx;
            seed.CommandText = """
                INSERT OR IGNORE INTO portfolio_group
                    (id, name, color_hex, description, icon_key, sort_order, default_cash_account_id, is_system, created_at, updated_at)
                VALUES
                    ($id, $name, NULL, $desc, NULL, 0, NULL, 1, $now, $now);
                """;
            seed.Parameters.AddWithValue("$id", PortfolioGroup.DefaultId.ToString());
            seed.Parameters.AddWithValue("$name", "預設群組");
            seed.Parameters.AddWithValue("$desc", "系統建立的預設群組。所有未指定群組的交易與現金帳戶都會掛在此群組下，可重新命名但不能刪除。");
            seed.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
            seed.ExecuteNonQuery();

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }
}
