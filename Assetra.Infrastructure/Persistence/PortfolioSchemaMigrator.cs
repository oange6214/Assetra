using Microsoft.Data.Sqlite;

namespace Assetra.Infrastructure.Persistence;

internal static class PortfolioSchemaMigrator
{
    private static readonly HashSet<string> AllowedColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        "asset_type", "created_at", "updated_at", "display_name", "currency", "is_active",
    };

    private static readonly HashSet<string> AllowedLegacyDropColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        "buy_price", "quantity", "buy_date",
    };

    private static readonly HashSet<string> AllowedTypeDefs = new(StringComparer.OrdinalIgnoreCase)
    {
        "TEXT NOT NULL DEFAULT 'Stock'",
        "TEXT NOT NULL DEFAULT ''",
        "TEXT NOT NULL DEFAULT 'TWD'",
        "INTEGER NOT NULL DEFAULT 1",
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
                CREATE TABLE IF NOT EXISTS portfolio (
                    id         TEXT PRIMARY KEY,
                    symbol     TEXT NOT NULL,
                    exchange   TEXT NOT NULL,
                    buy_price  REAL NOT NULL,
                    quantity   REAL NOT NULL,
                    buy_date   TEXT NOT NULL,
                    asset_type TEXT NOT NULL DEFAULT 'Stock'
                );
                """;
            cmd.ExecuteNonQuery();

            SqliteSchemaHelper.MigrateAddColumn(conn, tx, "portfolio",
                "asset_type", "TEXT NOT NULL DEFAULT 'Stock'", AllowedColumns, AllowedTypeDefs);
            SqliteSchemaHelper.MigrateAddColumn(conn, tx, "portfolio",
                "created_at", "TEXT NOT NULL DEFAULT ''", AllowedColumns, AllowedTypeDefs);
            SqliteSchemaHelper.MigrateAddColumn(conn, tx, "portfolio",
                "updated_at", "TEXT NOT NULL DEFAULT ''", AllowedColumns, AllowedTypeDefs);
            SqliteSchemaHelper.MigrateAddColumn(conn, tx, "portfolio",
                "display_name", "TEXT NOT NULL DEFAULT ''", AllowedColumns, AllowedTypeDefs);
            SqliteSchemaHelper.MigrateAddColumn(conn, tx, "portfolio",
                "currency", "TEXT NOT NULL DEFAULT 'TWD'", AllowedColumns, AllowedTypeDefs);
            SqliteSchemaHelper.MigrateAddColumn(conn, tx, "portfolio",
                "is_active", "INTEGER NOT NULL DEFAULT 1", AllowedColumns, AllowedTypeDefs);

            DeduplicateEntries(conn, tx);
            EnsureUniqueIndex(conn, tx);

            if (SqliteSchemaHelper.ColumnExists(conn, "portfolio", "buy_price", tx))
                VerifyProjectionMatchesStoredState(conn, tx);

            DropLegacyColumns(conn, tx);

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    private static void DeduplicateEntries(SqliteConnection conn, SqliteTransaction tx)
    {
        using var chk = conn.CreateCommand();
        chk.Transaction = tx;
        chk.CommandText =
            "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='idx_portfolio_symbol_exchange'";
        if ((long)chk.ExecuteScalar()! != 0)
            return;

        using (var tblChk = conn.CreateCommand())
        {
            tblChk.Transaction = tx;
            tblChk.CommandText =
                "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='trade'";
            if ((long)tblChk.ExecuteScalar()! > 0)
            {
                using var repoint = conn.CreateCommand();
                repoint.Transaction = tx;
                repoint.CommandText = """
                    UPDATE trade
                    SET portfolio_entry_id = (
                        SELECT MIN(p2.id)
                        FROM   portfolio p2
                        INNER JOIN portfolio p1
                            ON p1.symbol = p2.symbol AND p1.exchange = p2.exchange
                        WHERE  p1.id = trade.portfolio_entry_id
                    )
                    WHERE portfolio_entry_id IN (
                        SELECT id FROM portfolio
                        WHERE  id NOT IN (
                            SELECT MIN(id) FROM portfolio GROUP BY symbol, exchange
                        )
                    )
                    """;
                repoint.ExecuteNonQuery();
            }
        }

        using (var logChk = conn.CreateCommand())
        {
            logChk.Transaction = tx;
            logChk.CommandText =
                "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='portfolio_position_log'";
            if ((long)logChk.ExecuteScalar()! > 0)
            {
                using var repointLog = conn.CreateCommand();
                repointLog.Transaction = tx;
                repointLog.CommandText = """
                    UPDATE portfolio_position_log
                    SET position_id = (
                        SELECT MIN(p2.id)
                        FROM   portfolio p2
                        INNER JOIN portfolio p1
                            ON p1.symbol = p2.symbol AND p1.exchange = p2.exchange
                        WHERE  p1.id = portfolio_position_log.position_id
                    )
                    WHERE position_id IN (
                        SELECT id FROM portfolio
                        WHERE  id NOT IN (
                            SELECT MIN(id) FROM portfolio GROUP BY symbol, exchange
                        )
                    )
                    """;
                repointLog.ExecuteNonQuery();
            }
        }

        using var del = conn.CreateCommand();
        del.Transaction = tx;
        del.CommandText = """
            DELETE FROM portfolio
            WHERE id NOT IN (
                SELECT MIN(id) FROM portfolio GROUP BY symbol, exchange
            )
            """;
        del.ExecuteNonQuery();
    }

    private static void EnsureUniqueIndex(SqliteConnection conn, SqliteTransaction tx)
    {
        using var idx = conn.CreateCommand();
        idx.Transaction = tx;
        idx.CommandText = "CREATE UNIQUE INDEX IF NOT EXISTS idx_portfolio_symbol_exchange ON portfolio(symbol, exchange)";
        idx.ExecuteNonQuery();
    }

    private static void DropLegacyColumns(SqliteConnection conn, SqliteTransaction tx)
    {
        foreach (var col in AllowedLegacyDropColumns)
        {
            SqliteSchemaHelper.MigrateDropColumn(conn, tx, "portfolio", col, AllowedLegacyDropColumns);
        }
    }

    private static void VerifyProjectionMatchesStoredState(SqliteConnection conn, SqliteTransaction tx)
    {
        using (var count = conn.CreateCommand())
        {
            count.Transaction = tx;
            count.CommandText = "SELECT COUNT(*) FROM portfolio WHERE buy_price IS NOT NULL";
            var n = (long)count.ExecuteScalar()!;
            if (n == 0) return;
        }

        var entries = new List<(Guid Id, decimal StoredPrice, decimal StoredQty)>();
        using (var sel = conn.CreateCommand())
        {
            sel.Transaction = tx;
            sel.CommandText = "SELECT id, buy_price, quantity FROM portfolio WHERE buy_price IS NOT NULL";
            using var r = sel.ExecuteReader();
            while (r.Read())
            {
                entries.Add((
                    Guid.Parse(r.GetString(0)),
                    (decimal)r.GetDouble(1),
                    (decimal)r.GetDouble(2)));
            }
        }

        int matches = 0, mismatches = 0;
        foreach (var (id, storedPrice, storedQty) in entries)
        {
            decimal totalCost = 0m, totalQty = 0m;
            using (var sel = conn.CreateCommand())
            {
                sel.Transaction = tx;
                sel.CommandText = @"SELECT trade_type, price, quantity, commission FROM trade
                                    WHERE portfolio_entry_id = $id ORDER BY trade_date";
                sel.Parameters.AddWithValue("$id", id.ToString());
                using var r = sel.ExecuteReader();
                while (r.Read())
                {
                    var type = r.GetString(0);
                    var price = (decimal)r.GetDouble(1);
                    var qty = (decimal)r.GetInt64(2);
                    var commission = r.IsDBNull(3) ? 0m : (decimal)r.GetDouble(3);
                    if (type == "Buy") { totalCost += price * qty + commission; totalQty += qty; }
                    else if (type == "Sell" && totalQty > 0m)
                    {
                        totalCost -= totalCost * (qty / totalQty);
                        totalQty -= qty;
                    }
                    else if (type == "StockDividend") { totalQty += qty; }
                }
            }

            var projectedAvg = totalQty > 0m ? totalCost / totalQty : 0m;
            var qtyDiff = Math.Abs(totalQty - storedQty) / Math.Max(Math.Abs(storedQty), 1m);
            var costDiff = Math.Abs(projectedAvg - storedPrice) / Math.Max(Math.Abs(storedPrice), 1m);
            if (qtyDiff > 0.0001m || costDiff > 0.0001m) mismatches++;
            else matches++;
        }

        System.Diagnostics.Debug.WriteLine(
            $"[Wave 9 verification] {matches} entries matched, {mismatches} mismatches.");
    }

}
