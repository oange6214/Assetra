using Microsoft.Data.Sqlite;

namespace Assetra.Infrastructure.Persistence;

internal static class PortfolioSnapshotSchemaMigrator
{
    public static void EnsureInitialized(string connectionString)
    {
        using var conn = new SqliteConnection(connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS portfolio_daily_snapshot (
                snapshot_date  TEXT NOT NULL PRIMARY KEY,
                total_cost     REAL NOT NULL,
                market_value   REAL NOT NULL,
                pnl            REAL NOT NULL,
                position_count INTEGER NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();
    }
}
