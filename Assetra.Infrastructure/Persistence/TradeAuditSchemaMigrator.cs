using Microsoft.Data.Sqlite;

namespace Assetra.Infrastructure.Persistence;

/// <summary>
/// Schema migrator for the append-only <c>trade_audit</c> table.
/// Creates the table if missing; future column additions follow the
/// add-column-via-PRAGMA pattern used by <see cref="TradeSchemaMigrator"/>.
/// </summary>
internal static class TradeAuditSchemaMigrator
{
    public static void EnsureInitialized(string connectionString)
    {
        using var conn = new SqliteConnection(connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS trade_audit (
                id           TEXT PRIMARY KEY,
                trade_id     TEXT NOT NULL,
                action       TEXT NOT NULL,
                trade_json   TEXT NOT NULL,
                recorded_at  TEXT NOT NULL,
                note         TEXT
            );
            CREATE INDEX IF NOT EXISTS idx_trade_audit_trade_id  ON trade_audit (trade_id);
            CREATE INDEX IF NOT EXISTS idx_trade_audit_recorded  ON trade_audit (recorded_at DESC);
            """;
        cmd.ExecuteNonQuery();
    }
}
