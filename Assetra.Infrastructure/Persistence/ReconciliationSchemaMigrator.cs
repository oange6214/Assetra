using Microsoft.Data.Sqlite;

namespace Assetra.Infrastructure.Persistence;

internal static class ReconciliationSchemaMigrator
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
                CREATE TABLE IF NOT EXISTS reconciliation_session (
                    id                  TEXT PRIMARY KEY,
                    account_id          TEXT NOT NULL,
                    period_start        TEXT NOT NULL,
                    period_end          TEXT NOT NULL,
                    source_batch_id     TEXT,
                    created_at          TEXT NOT NULL,
                    status              INTEGER NOT NULL,
                    note                TEXT,
                    statement_rows_json TEXT
                );

                CREATE INDEX IF NOT EXISTS idx_recon_session_account
                    ON reconciliation_session (account_id, period_start DESC);

                CREATE TABLE IF NOT EXISTS reconciliation_diff (
                    id                  TEXT PRIMARY KEY,
                    session_id          TEXT NOT NULL,
                    kind                INTEGER NOT NULL,
                    statement_row_json  TEXT,
                    trade_id            TEXT,
                    resolution          INTEGER NOT NULL,
                    resolved_at         TEXT,
                    note                TEXT,
                    FOREIGN KEY (session_id) REFERENCES reconciliation_session (id) ON DELETE CASCADE
                );

                CREATE INDEX IF NOT EXISTS idx_recon_diff_session
                    ON reconciliation_diff (session_id);
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
