using Microsoft.Data.Sqlite;

namespace Assetra.Infrastructure.Persistence;

/// <summary>
/// Creates the <c>fx_rate_history</c> table — the per-date FX quote store that
/// backs multi-currency historical aggregation. PK = (date, base_ccy, quote_ccy)
/// guarantees there's exactly one row per quote per day.
/// </summary>
internal static class FxRateHistorySchemaMigrator
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
                CREATE TABLE IF NOT EXISTS fx_rate_history (
                    date          TEXT NOT NULL,         -- yyyy-MM-dd, UTC date
                    base_ccy      TEXT NOT NULL,         -- from-side ISO 4217
                    quote_ccy     TEXT NOT NULL,         -- to-side ISO 4217
                    rate          REAL NOT NULL,         -- 1 base = rate quote
                    source        TEXT NOT NULL DEFAULT 'manual',
                    ingested_at   TEXT NOT NULL,         -- UTC ISO 8601
                    PRIMARY KEY (date, base_ccy, quote_ccy)
                );
                """;
            cmd.ExecuteNonQuery();

            // Hot-path index for nearest-date lookups: scan backwards along the
            // partial PK prefix (base_ccy, quote_ccy, date) and stop at first hit.
            using var idx = conn.CreateCommand();
            idx.Transaction = tx;
            idx.CommandText = """
                CREATE INDEX IF NOT EXISTS idx_fx_rate_history_pair_date
                    ON fx_rate_history(base_ccy, quote_ccy, date);
                """;
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
