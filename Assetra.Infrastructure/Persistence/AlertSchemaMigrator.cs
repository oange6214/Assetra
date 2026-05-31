using Microsoft.Data.Sqlite;

namespace Assetra.Infrastructure.Persistence;

internal static class AlertSchemaMigrator
{
    // 同步擴充欄位允許清單（Sync-Status-Indicator 補洞）— 跟 RetirementSchemaMigrator 對齊。
    private static readonly HashSet<string> AllowedColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        "created_at", "updated_at",
        "ev_version", "ev_modified_at", "ev_device_id",
        "is_deleted", "is_pending_push",
    };

    private static readonly HashSet<string> AllowedTypeDefs = new(StringComparer.OrdinalIgnoreCase)
    {
        "TEXT NOT NULL DEFAULT ''",
        "INTEGER NOT NULL DEFAULT 0",
        "INTEGER NOT NULL DEFAULT 1",
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
                    id              TEXT PRIMARY KEY,
                    symbol          TEXT NOT NULL,
                    exchange        TEXT NOT NULL,
                    condition       INTEGER NOT NULL,
                    target_price    REAL NOT NULL,
                    is_triggered    INTEGER NOT NULL DEFAULT 0,
                    trigger_time    TEXT,
                    created_at      TEXT NOT NULL DEFAULT '',
                    updated_at      TEXT NOT NULL DEFAULT '',
                    ev_version      INTEGER NOT NULL DEFAULT 0,
                    ev_modified_at  TEXT NOT NULL DEFAULT '',
                    ev_device_id    TEXT NOT NULL DEFAULT '',
                    is_deleted      INTEGER NOT NULL DEFAULT 0,
                    is_pending_push INTEGER NOT NULL DEFAULT 1
                );
                """;
            cmd.ExecuteNonQuery();

            MigrateLegacyTable(cmd);

            // 既有 DB 升級：新欄位用 MigrateAddColumn idempotent 補
            SqliteSchemaHelper.MigrateAddColumn(conn, tx, "alert",
                "created_at", "TEXT NOT NULL DEFAULT ''", AllowedColumns, AllowedTypeDefs);
            SqliteSchemaHelper.MigrateAddColumn(conn, tx, "alert",
                "updated_at", "TEXT NOT NULL DEFAULT ''", AllowedColumns, AllowedTypeDefs);
            SqliteSchemaHelper.MigrateAddColumn(conn, tx, "alert",
                "ev_version", "INTEGER NOT NULL DEFAULT 0", AllowedColumns, AllowedTypeDefs);
            SqliteSchemaHelper.MigrateAddColumn(conn, tx, "alert",
                "ev_modified_at", "TEXT NOT NULL DEFAULT ''", AllowedColumns, AllowedTypeDefs);
            SqliteSchemaHelper.MigrateAddColumn(conn, tx, "alert",
                "ev_device_id", "TEXT NOT NULL DEFAULT ''", AllowedColumns, AllowedTypeDefs);
            SqliteSchemaHelper.MigrateAddColumn(conn, tx, "alert",
                "is_deleted", "INTEGER NOT NULL DEFAULT 0", AllowedColumns, AllowedTypeDefs);
            SqliteSchemaHelper.MigrateAddColumn(conn, tx, "alert",
                "is_pending_push", "INTEGER NOT NULL DEFAULT 1", AllowedColumns, AllowedTypeDefs);

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
        if (cmd.ExecuteScalar() is null)
            return;

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
