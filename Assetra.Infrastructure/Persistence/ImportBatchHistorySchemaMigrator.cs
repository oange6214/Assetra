using Microsoft.Data.Sqlite;

namespace Assetra.Infrastructure.Persistence;

internal static class ImportBatchHistorySchemaMigrator
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
                CREATE TABLE IF NOT EXISTS import_batch_history (
                    id                TEXT PRIMARY KEY,
                    batch_id          TEXT NOT NULL,
                    file_name         TEXT NOT NULL,
                    format            INTEGER NOT NULL,
                    applied_at        TEXT NOT NULL,
                    rows_applied      INTEGER NOT NULL,
                    rows_skipped      INTEGER NOT NULL,
                    rows_overwritten  INTEGER NOT NULL,
                    is_rolled_back    INTEGER NOT NULL DEFAULT 0,
                    rolled_back_at    TEXT
                );

                CREATE INDEX IF NOT EXISTS idx_import_history_applied_at
                    ON import_batch_history (applied_at DESC);

                CREATE TABLE IF NOT EXISTS import_batch_entry (
                    id                          TEXT PRIMARY KEY,
                    history_id                  TEXT NOT NULL,
                    row_index                   INTEGER NOT NULL,
                    action                      INTEGER NOT NULL,
                    new_trade_id                TEXT,
                    overwritten_trade_json      TEXT,
                    FOREIGN KEY (history_id) REFERENCES import_batch_history (id) ON DELETE CASCADE
                );

                CREATE INDEX IF NOT EXISTS idx_import_entry_history
                    ON import_batch_entry (history_id);
                """;
            cmd.ExecuteNonQuery();

            SqliteSchemaHelper.MigrateAddColumn(
                conn, tx,
                table: "import_batch_entry",
                column: "preview_row_json",
                typeDef: "TEXT",
                allowedColumns: new(StringComparer.OrdinalIgnoreCase) { "preview_row_json" },
                allowedTypeDefs: new(StringComparer.OrdinalIgnoreCase) { "TEXT" });

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }
}
