using Microsoft.Data.Sqlite;

namespace Assetra.Infrastructure.Persistence;

internal static class PortfolioPositionLogSchemaMigrator
{
    public static void EnsureInitialized(string connectionString)
    {
        using var conn = new SqliteConnection(connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS portfolio_position_log (
                log_id      TEXT    NOT NULL PRIMARY KEY,
                log_date    TEXT    NOT NULL,
                position_id TEXT    NOT NULL,
                symbol      TEXT    NOT NULL,
                exchange    TEXT    NOT NULL,
                quantity    INTEGER NOT NULL,
                buy_price   REAL    NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_ppl_position_date
                ON portfolio_position_log (position_id, log_date);
            """;
        cmd.ExecuteNonQuery();
    }
}
