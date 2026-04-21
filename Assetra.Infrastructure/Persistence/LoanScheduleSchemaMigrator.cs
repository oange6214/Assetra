using Microsoft.Data.Sqlite;

namespace Assetra.Infrastructure.Persistence;

internal static class LoanScheduleSchemaMigrator
{
    private static readonly HashSet<string> AllowedColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        "is_paid", "paid_at", "trade_id",
    };

    private static readonly HashSet<string> AllowedTypeDefs = new(StringComparer.OrdinalIgnoreCase)
    {
        "INTEGER NOT NULL DEFAULT 0",
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
                CREATE TABLE IF NOT EXISTS loan_schedule (
                    id               TEXT PRIMARY KEY,
                    asset_id         TEXT NOT NULL REFERENCES asset(id),
                    period           INTEGER NOT NULL,
                    due_date         TEXT NOT NULL,
                    total_amount     REAL NOT NULL,
                    principal_amount REAL NOT NULL,
                    interest_amount  REAL NOT NULL,
                    remaining        REAL NOT NULL,
                    is_paid          INTEGER NOT NULL DEFAULT 0,
                    paid_at          TEXT,
                    trade_id         TEXT,
                    UNIQUE (asset_id, period)
                );
                CREATE INDEX IF NOT EXISTS idx_loan_schedule_asset ON loan_schedule (asset_id);
                """;
            cmd.ExecuteNonQuery();

            SqliteSchemaHelper.MigrateAddColumn(conn, tx, "loan_schedule",
                "is_paid", "INTEGER NOT NULL DEFAULT 0", AllowedColumns, AllowedTypeDefs);
            SqliteSchemaHelper.MigrateAddColumn(conn, tx, "loan_schedule",
                "paid_at", "TEXT", AllowedColumns, AllowedTypeDefs);
            SqliteSchemaHelper.MigrateAddColumn(conn, tx, "loan_schedule",
                "trade_id", "TEXT", AllowedColumns, AllowedTypeDefs);

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }
}
