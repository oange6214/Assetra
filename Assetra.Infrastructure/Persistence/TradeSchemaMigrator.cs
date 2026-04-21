using Microsoft.Data.Sqlite;

namespace Assetra.Infrastructure.Persistence;

internal static class TradeSchemaMigrator
{
    private static readonly HashSet<string> AllowedColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        "cash_amount", "cash_account_id", "note", "portfolio_entry_id",
        "created_at", "updated_at", "commission", "commission_discount",
        "liability_account_id", "principal", "interest_paid", "to_cash_account_id",
        "loan_label", "parent_trade_id",
    };

    private static readonly HashSet<string> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "REAL", "TEXT", "TEXT NOT NULL DEFAULT ''",
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
                CREATE TABLE IF NOT EXISTS trade (
                    id                   TEXT PRIMARY KEY,
                    symbol               TEXT NOT NULL,
                    exchange             TEXT NOT NULL,
                    name                 TEXT NOT NULL,
                    trade_type           TEXT NOT NULL,
                    trade_date           TEXT NOT NULL,
                    price                REAL NOT NULL,
                    quantity             INTEGER NOT NULL,
                    realized_pnl         REAL,
                    realized_pnl_pct     REAL,
                    cash_amount          REAL,
                    cash_account_id      TEXT,
                    note                 TEXT
                );
                CREATE INDEX IF NOT EXISTS idx_trade_symbol ON trade (symbol);
                CREATE INDEX IF NOT EXISTS idx_trade_date   ON trade (trade_date DESC);
                """;
            cmd.ExecuteNonQuery();

            MigrateAddColumn(conn, tx, "cash_amount", "REAL");
            MigrateAddColumn(conn, tx, "cash_account_id", "TEXT");
            MigrateAddColumn(conn, tx, "note", "TEXT");
            MigrateAddColumn(conn, tx, "portfolio_entry_id", "TEXT");
            MigrateAddColumn(conn, tx, "created_at", "TEXT NOT NULL DEFAULT ''");
            MigrateAddColumn(conn, tx, "updated_at", "TEXT NOT NULL DEFAULT ''");
            MigrateAddColumn(conn, tx, "commission", "REAL");
            MigrateAddColumn(conn, tx, "commission_discount", "REAL");
            MigrateAddColumn(conn, tx, "liability_account_id", "TEXT");
            MigrateAddColumn(conn, tx, "principal", "REAL");
            MigrateAddColumn(conn, tx, "interest_paid", "REAL");
            MigrateAddColumn(conn, tx, "to_cash_account_id", "TEXT");
            MigrateAddColumn(conn, tx, "loan_label", "TEXT");
            MigrateAddColumn(conn, tx, "parent_trade_id", "TEXT");

            cmd.CommandText = """
                CREATE INDEX IF NOT EXISTS idx_trade_cash_acct ON trade (cash_account_id);
                CREATE INDEX IF NOT EXISTS idx_trade_loan_label ON trade (loan_label);
                """;
            cmd.ExecuteNonQuery();

            BackfillLegacyLiabilityLinks(conn, tx, cmd);
            BackfillLoanLabels(conn, tx, cmd);
            NormalizeLegacyInterestTrades(cmd);

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    private static void BackfillLegacyLiabilityLinks(
        SqliteConnection conn, SqliteTransaction tx, SqliteCommand cmd)
    {
        if (!TableExists(conn, tx, "liability_account"))
            return;

        cmd.CommandText = """
            UPDATE trade
               SET liability_account_id = (
                   SELECT id FROM liability_account
                    WHERE liability_account.name = trade.name
                    LIMIT 1
               )
             WHERE trade_type = 'LoanBorrow'
               AND liability_account_id IS NULL
               AND name IN (SELECT name FROM liability_account);
            """;
        cmd.ExecuteNonQuery();
    }

    private static void BackfillLoanLabels(
        SqliteConnection conn, SqliteTransaction tx, SqliteCommand cmd)
    {
        if (TableExists(conn, tx, "liability_account"))
        {
            cmd.CommandText = """
                UPDATE trade
                   SET loan_label = (
                       SELECT name FROM liability_account
                        WHERE liability_account.id = trade.liability_account_id
                        LIMIT 1
                   )
                 WHERE trade_type IN ('LoanBorrow', 'LoanRepay')
                   AND liability_account_id IS NOT NULL
                   AND loan_label IS NULL;
                """;
            cmd.ExecuteNonQuery();
            return;
        }

        if (!TableExists(conn, tx, "asset"))
            return;

        cmd.CommandText = """
            UPDATE trade
               SET loan_label = (
                   SELECT name FROM asset
                    WHERE asset.id = trade.liability_account_id
                    LIMIT 1
               )
             WHERE trade_type IN ('LoanBorrow', 'LoanRepay')
               AND liability_account_id IS NOT NULL
               AND loan_label IS NULL;
            """;
        cmd.ExecuteNonQuery();
    }

    private static void NormalizeLegacyInterestTrades(SqliteCommand cmd)
    {
        cmd.CommandText = """
            UPDATE trade
               SET trade_type  = 'Withdrawal',
                   cash_amount = CASE
                       WHEN cash_amount IS NULL THEN NULL
                       ELSE ABS(cash_amount)
                   END
             WHERE trade_type = 'Interest';
            """;
        cmd.ExecuteNonQuery();
    }

    private static void MigrateAddColumn(
        SqliteConnection conn, SqliteTransaction tx, string column, string type)
    {
        if (!AllowedColumns.Contains(column) || !AllowedTypes.Contains(type))
            throw new ArgumentException($"Invalid column or type: {column} {type}");

        using var check = conn.CreateCommand();
        check.Transaction = tx;
        check.CommandText = "SELECT COUNT(*) FROM pragma_table_info('trade') WHERE name = $col;";
        check.Parameters.AddWithValue("$col", column);
        if ((long)(check.ExecuteScalar() ?? 0L) > 0)
            return;

        using var alter = conn.CreateCommand();
        alter.Transaction = tx;
        alter.CommandText = $"ALTER TABLE trade ADD COLUMN {column} {type};";
        alter.ExecuteNonQuery();
    }

    private static bool TableExists(SqliteConnection conn, SqliteTransaction tx, string tableName)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $name LIMIT 1;";
        cmd.Parameters.AddWithValue("$name", tableName);
        return cmd.ExecuteScalar() is not null;
    }
}
