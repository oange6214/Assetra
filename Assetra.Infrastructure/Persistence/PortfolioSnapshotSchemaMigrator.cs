using Microsoft.Data.Sqlite;

namespace Assetra.Infrastructure.Persistence;

internal static class PortfolioSnapshotSchemaMigrator
{
    public static void EnsureInitialized(string connectionString)
    {
        using var conn = new SqliteConnection(connectionString);
        conn.Open();

        using (var create = conn.CreateCommand())
        {
            create.CommandText = """
                CREATE TABLE IF NOT EXISTS portfolio_daily_snapshot (
                    snapshot_date    TEXT NOT NULL PRIMARY KEY,
                    total_cost       REAL NOT NULL,
                    market_value     REAL NOT NULL,
                    pnl              REAL NOT NULL,
                    position_count   INTEGER NOT NULL,
                    currency         TEXT NOT NULL DEFAULT 'TWD',
                    cash_value       REAL,
                    equity_value     REAL,
                    liability_value  REAL
                );
                """;
            create.ExecuteNonQuery();
        }

        // v0.14.2: backfill currency column for databases created before the column existed.
        if (!ColumnExists(conn, "portfolio_daily_snapshot", "currency"))
        {
            using var alter = conn.CreateCommand();
            alter.CommandText = "ALTER TABLE portfolio_daily_snapshot ADD COLUMN currency TEXT NOT NULL DEFAULT 'TWD';";
            alter.ExecuteNonQuery();
        }

        // v0.17.1: add stacked-chart breakdown columns; nullable for backward compat.
        EnsureColumn(conn, "portfolio_daily_snapshot", "cash_value", "REAL");
        EnsureColumn(conn, "portfolio_daily_snapshot", "equity_value", "REAL");
        EnsureColumn(conn, "portfolio_daily_snapshot", "liability_value", "REAL");
    }

    private static void EnsureColumn(SqliteConnection conn, string table, string column, string typeDecl)
    {
        if (ColumnExists(conn, table, column)) return;
        using var alter = conn.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {typeDecl};";
        alter.ExecuteNonQuery();
    }

    private static bool ColumnExists(SqliteConnection conn, string table, string column)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name = $name;";
        cmd.Parameters.AddWithValue("$name", column);
        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
    }
}
