using Microsoft.Data.Sqlite;

namespace Assetra.Infrastructure.Persistence;

internal static class CategorySchemaMigrator
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
                CREATE TABLE IF NOT EXISTS expense_category (
                    id           TEXT PRIMARY KEY,
                    name         TEXT NOT NULL,
                    kind         TEXT NOT NULL,
                    parent_id    TEXT,
                    icon         TEXT,
                    color_hex    TEXT,
                    sort_order   INTEGER NOT NULL DEFAULT 0,
                    is_archived  INTEGER NOT NULL DEFAULT 0,
                    created_at   TEXT NOT NULL DEFAULT '',
                    updated_at   TEXT NOT NULL DEFAULT ''
                );
                CREATE INDEX IF NOT EXISTS idx_category_parent ON expense_category (parent_id);
                CREATE INDEX IF NOT EXISTS idx_category_kind   ON expense_category (kind);
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
