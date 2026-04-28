using Microsoft.Data.Sqlite;

namespace Assetra.Infrastructure.Persistence;

internal static class PortfolioEventSchemaMigrator
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
                CREATE TABLE IF NOT EXISTS portfolio_event (
                    id          TEXT PRIMARY KEY,
                    event_date  TEXT NOT NULL,
                    kind        TEXT NOT NULL,
                    label       TEXT NOT NULL,
                    description TEXT,
                    amount      REAL,
                    symbol      TEXT
                );
                """;
            cmd.ExecuteNonQuery();

            using var idx = conn.CreateCommand();
            idx.Transaction = tx;
            idx.CommandText =
                "CREATE INDEX IF NOT EXISTS idx_portfolio_event_date ON portfolio_event(event_date);";
            idx.ExecuteNonQuery();

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }
}
