using Microsoft.Data.Sqlite;

namespace Assetra.Infrastructure.Persistence;

internal static class AlertSchemaMigrator
{
    private static readonly HashSet<string> AllowedColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        "created_at", "updated_at",
    };

    private static readonly HashSet<string> AllowedTypeDefs = new(StringComparer.OrdinalIgnoreCase)
    {
        "TEXT NOT NULL DEFAULT ''",
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
                CREATE TABLE IF NOT EXISTS alert (
                    id           TEXT PRIMARY KEY,
                    symbol       TEXT NOT NULL,
                    exchange     TEXT NOT NULL,
                    condition    INTEGER NOT NULL,
                    target_price REAL NOT NULL,
                    is_triggered INTEGER NOT NULL DEFAULT 0,
                    trigger_time TEXT
                );
                """;
            cmd.ExecuteNonQuery();

            MigrateLegacyTable(cmd);
            SqliteSchemaHelper.MigrateAddColumn(conn, tx, "alert",
                "created_at", "TEXT NOT NULL DEFAULT ''", AllowedColumns, AllowedTypeDefs);
            SqliteSchemaHelper.MigrateAddColumn(conn, tx, "alert",
                "updated_at", "TEXT NOT NULL DEFAULT ''", AllowedColumns, AllowedTypeDefs);

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    private static void MigrateLegacyTable(SqliteCommand cmd)
    {
        // Probe sqlite_master first so we don't throw a first-chance "no such table" exception
        // on the common fresh-install path (legacy `alerts` table absent).
        cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name='alerts' LIMIT 1;";
        if (cmd.ExecuteScalar() is null) return;

        cmd.CommandText = """
            INSERT OR IGNORE INTO alert SELECT * FROM alerts;
            DROP TABLE IF EXISTS alerts;
            """;
        try
        {
            cmd.ExecuteNonQuery();
        }
        catch
        {
            // Legacy shape does not match current schema; ignore to keep init idempotent.
        }
    }
}
