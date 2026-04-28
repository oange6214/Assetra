using Microsoft.Data.Sqlite;

namespace Assetra.Infrastructure.Persistence;

internal static class FxRateSchemaMigrator
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
                CREATE TABLE IF NOT EXISTS fx_rate (
                    from_ccy    TEXT NOT NULL,
                    to_ccy      TEXT NOT NULL,
                    as_of_date  TEXT NOT NULL,
                    rate        REAL NOT NULL,
                    PRIMARY KEY (from_ccy, to_ccy, as_of_date)
                );
                CREATE INDEX IF NOT EXISTS idx_fx_rate_pair_date
                    ON fx_rate (from_ccy, to_ccy, as_of_date);
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
