using Microsoft.Data.Sqlite;

namespace Assetra.Infrastructure.Persistence;

internal static class EquityOhlcCacheSchemaMigrator
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
                CREATE TABLE IF NOT EXISTS equity_ohlc_cache (
                    symbol            TEXT NOT NULL,
                    exchange          TEXT NOT NULL,
                    interval          TEXT NOT NULL,
                    trade_date        TEXT NOT NULL,
                    open              REAL NOT NULL,
                    high              REAL NOT NULL,
                    low               REAL NOT NULL,
                    close             REAL NOT NULL,
                    volume            INTEGER NOT NULL,
                    currency          TEXT NOT NULL,
                    source_provider   TEXT NOT NULL,
                    source_updated_at TEXT NOT NULL,
                    is_adjusted       INTEGER NOT NULL,
                    PRIMARY KEY(symbol, exchange, interval, trade_date)
                );
                CREATE INDEX IF NOT EXISTS idx_equity_ohlc_cache_range
                    ON equity_ohlc_cache (symbol, exchange, interval, trade_date);
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
