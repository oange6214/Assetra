using Microsoft.Data.Sqlite;

namespace Assetra.Infrastructure.Persistence;

internal static class BudgetSchemaMigrator
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
                CREATE TABLE IF NOT EXISTS budget (
                    id          TEXT PRIMARY KEY,
                    category_id TEXT NULL,
                    mode        INTEGER NOT NULL,
                    year        INTEGER NOT NULL,
                    month       INTEGER NULL,
                    amount      TEXT NOT NULL,
                    currency    TEXT NOT NULL DEFAULT 'TWD',
                    note        TEXT NULL,
                    created_at  TEXT NOT NULL DEFAULT '',
                    updated_at  TEXT NOT NULL DEFAULT ''
                );
                CREATE INDEX IF NOT EXISTS idx_budget_period ON budget (year, month);
                CREATE INDEX IF NOT EXISTS idx_budget_category ON budget (category_id);
                """;
            cmd.ExecuteNonQuery();
            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }
}
