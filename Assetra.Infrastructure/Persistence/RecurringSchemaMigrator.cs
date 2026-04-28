using Microsoft.Data.Sqlite;

namespace Assetra.Infrastructure.Persistence;

internal static class RecurringSchemaMigrator
{
    private static readonly HashSet<string> AllowedColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        "version", "last_modified_at", "last_modified_by_device", "is_deleted", "is_pending_push",
    };

    private static readonly HashSet<string> AllowedTypeDefs = new(StringComparer.OrdinalIgnoreCase)
    {
        "TEXT NOT NULL DEFAULT ''",
        "INTEGER NOT NULL DEFAULT 0",
    };

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
                    CREATE TABLE IF NOT EXISTS recurring_transaction (
                        id                 TEXT PRIMARY KEY,
                        name               TEXT NOT NULL,
                        trade_type         INTEGER NOT NULL,
                        amount             TEXT NOT NULL,
                        cash_account_id    TEXT NULL,
                        category_id        TEXT NULL,
                        frequency          INTEGER NOT NULL,
                        interval_value     INTEGER NOT NULL DEFAULT 1,
                        start_date         TEXT NOT NULL,
                        end_date           TEXT NULL,
                        generation_mode    INTEGER NOT NULL,
                        last_generated_at  TEXT NULL,
                        next_due_at        TEXT NULL,
                        note               TEXT NULL,
                        is_enabled         INTEGER NOT NULL DEFAULT 1,
                        created_at         TEXT NOT NULL DEFAULT '',
                        updated_at         TEXT NOT NULL DEFAULT ''
                    );
                    CREATE INDEX IF NOT EXISTS idx_recurring_next_due ON recurring_transaction (next_due_at);
                    CREATE INDEX IF NOT EXISTS idx_recurring_enabled ON recurring_transaction (is_enabled);

                    CREATE TABLE IF NOT EXISTS pending_recurring_entry (
                        id                  TEXT PRIMARY KEY,
                        recurring_source_id TEXT NOT NULL,
                        due_date            TEXT NOT NULL,
                        amount              TEXT NOT NULL,
                        trade_type          INTEGER NOT NULL,
                        cash_account_id     TEXT NULL,
                        category_id         TEXT NULL,
                        note                TEXT NULL,
                        status              INTEGER NOT NULL DEFAULT 0,
                        generated_trade_id  TEXT NULL,
                        resolved_at         TEXT NULL,
                        created_at          TEXT NOT NULL DEFAULT ''
                    );
                    CREATE INDEX IF NOT EXISTS idx_pending_status ON pending_recurring_entry (status);
                    CREATE INDEX IF NOT EXISTS idx_pending_source ON pending_recurring_entry (recurring_source_id);
                    """;
                cmd.ExecuteNonQuery();
            }

            // v0.20.11: cloud sync columns on recurring_transaction. PendingRecurringEntry is a
            // device-local materialized queue (resolved_at is set per-device), so it intentionally
            // stays out of sync.
            SqliteSchemaHelper.MigrateAddColumn(conn, tx, "recurring_transaction",
                "version", "INTEGER NOT NULL DEFAULT 0", AllowedColumns, AllowedTypeDefs);
            SqliteSchemaHelper.MigrateAddColumn(conn, tx, "recurring_transaction",
                "last_modified_at", "TEXT NOT NULL DEFAULT ''", AllowedColumns, AllowedTypeDefs);
            SqliteSchemaHelper.MigrateAddColumn(conn, tx, "recurring_transaction",
                "last_modified_by_device", "TEXT NOT NULL DEFAULT ''", AllowedColumns, AllowedTypeDefs);
            SqliteSchemaHelper.MigrateAddColumn(conn, tx, "recurring_transaction",
                "is_deleted", "INTEGER NOT NULL DEFAULT 0", AllowedColumns, AllowedTypeDefs);
            SqliteSchemaHelper.MigrateAddColumn(conn, tx, "recurring_transaction",
                "is_pending_push", "INTEGER NOT NULL DEFAULT 0", AllowedColumns, AllowedTypeDefs);

            using (var idx = conn.CreateCommand())
            {
                idx.Transaction = tx;
                idx.CommandText =
                    "CREATE INDEX IF NOT EXISTS idx_recurring_pending ON recurring_transaction (is_pending_push) WHERE is_pending_push = 1;";
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
}
