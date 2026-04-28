using Microsoft.Data.Sqlite;

namespace Assetra.Infrastructure.Persistence;

internal static class CategorySchemaMigrator
{
    /// <summary>
    /// 確保 <c>expense_category</c> table 存在並具備 v0.20.4 雲端同步所需的欄位：
    /// <c>version</c> / <c>last_modified_at</c> / <c>last_modified_by_device</c> /
    /// <c>is_deleted</c>（tombstone）/ <c>is_pending_push</c>（outbox flag）。
    /// 既有 DB 透過 PRAGMA table_info 檢測缺漏欄位、以 ALTER TABLE 補齊（零資料遷移腳本）。
    /// </summary>
    public static void EnsureInitialized(string connectionString)
    {
        using var conn = new SqliteConnection(connectionString);
        conn.Open();
        using var tx = conn.BeginTransaction();
        try
        {
            using (var cmd = conn.CreateCommand())
            {
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
            }

            AddColumnIfMissing(conn, tx, "version", "INTEGER NOT NULL DEFAULT 0");
            AddColumnIfMissing(conn, tx, "last_modified_at", "TEXT NOT NULL DEFAULT ''");
            AddColumnIfMissing(conn, tx, "last_modified_by_device", "TEXT NOT NULL DEFAULT ''");
            AddColumnIfMissing(conn, tx, "is_deleted", "INTEGER NOT NULL DEFAULT 0");
            AddColumnIfMissing(conn, tx, "is_pending_push", "INTEGER NOT NULL DEFAULT 0");

            using (var idx = conn.CreateCommand())
            {
                idx.Transaction = tx;
                idx.CommandText = """
                    CREATE INDEX IF NOT EXISTS idx_category_pending ON expense_category (is_pending_push);
                    """;
                idx.ExecuteNonQuery();
            }

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    private static void AddColumnIfMissing(SqliteConnection conn, SqliteTransaction tx, string column, string definition)
    {
        using var probe = conn.CreateCommand();
        probe.Transaction = tx;
        probe.CommandText = "SELECT 1 FROM pragma_table_info('expense_category') WHERE name = $name;";
        probe.Parameters.AddWithValue("$name", column);
        var exists = probe.ExecuteScalar() is not null;
        if (exists) return;

        using var alter = conn.CreateCommand();
        alter.Transaction = tx;
        alter.CommandText = $"ALTER TABLE expense_category ADD COLUMN {column} {definition};";
        alter.ExecuteNonQuery();
    }
}
