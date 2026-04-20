using Microsoft.Data.Sqlite;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;

namespace Assetra.Infrastructure.Persistence;

public sealed class PortfolioSqliteRepository : IPortfolioRepository
{
    private readonly string _connectionString;

    public PortfolioSqliteRepository(string dbPath)
    {
        _connectionString = $"Data Source={dbPath}";
        Initialize();
    }

    private static readonly HashSet<string> AllowedColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        "asset_type", "created_at", "updated_at", "display_name", "currency", "is_active",
    };

    private static readonly HashSet<string> AllowedTypeDefs = new(StringComparer.OrdinalIgnoreCase)
    {
        "TEXT NOT NULL DEFAULT 'Stock'",
        "TEXT NOT NULL DEFAULT ''",
        "TEXT NOT NULL DEFAULT 'TWD'",
        "INTEGER NOT NULL DEFAULT 1",
    };

    private void Initialize()
    {
        using var conn = new SqliteConnection(_connectionString);
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

            // Idempotent column migrations — all inside the same transaction so the
            // schema either succeeds atomically or rolls back cleanly.
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

            // Wave 9.1-pre: deduplicate (symbol, exchange) before enforcing UNIQUE constraint.
            // Runs only once (skipped when the index already exists).
            // Existing databases could have multiple portfolio rows for the same symbol from
            // pre-refactor usage.  Strategy: pick the oldest row (MIN id) as the winner for
            // each group, re-point all FK references to it, then delete the losers.
            using (var chk = conn.CreateCommand())
            {
                chk.Transaction = tx;
                chk.CommandText =
                    "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='idx_portfolio_symbol_exchange'";
                if ((long)chk.ExecuteScalar()! == 0)
                {
                    // Re-point trade.portfolio_entry_id from loser IDs to the winner.
                    // Skipped when trade table does not yet exist (fresh-install ordering).
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

                    // Re-point portfolio_position_log.position_id from loser IDs to the winner.
                    // Skipped when the log table does not yet exist.
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

                    // Delete all but the winner (MIN id) per (symbol, exchange) group.
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
            }

            using (var idx = conn.CreateCommand())
            {
                idx.Transaction = tx;
                idx.CommandText = "CREATE UNIQUE INDEX IF NOT EXISTS idx_portfolio_symbol_exchange ON portfolio(symbol, exchange)";
                idx.ExecuteNonQuery();
            }

            // Wave 9.2: non-blocking verification that the Running Balance projection
            // agrees with the stored buy_price/quantity columns before Wave 9.3 drops
            // them. Mismatches are logged to Debug, not thrown — this is diagnostic only.
            // Guard: skip entirely when buy_price has already been dropped (Wave 9.3+).
            if (ColumnExists(conn, tx, "portfolio", "buy_price"))
                VerifyProjectionMatchesStoredState(conn, tx);

            // Wave 9.3: drop financial columns — source of truth is now trade projection
            foreach (var col in new[] { "buy_price", "quantity", "buy_date" })
            {
                if (ColumnExists(conn, tx, "portfolio", col))
                {
                    using var drop = conn.CreateCommand();
                    drop.Transaction = tx;
                    drop.CommandText = $"ALTER TABLE portfolio DROP COLUMN {col}";
                    drop.ExecuteNonQuery();
                }
            }

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    /// <summary>
    /// Wave 9.2 — Non-blocking parity check. Reads each portfolio row's legacy
    /// (buy_price, quantity) fields and compares against a simplified inline projection
    /// of its trade log. Logs match/mismatch counts via <see cref="System.Diagnostics.Debug"/>.
    /// Early-returns on fresh databases with no stored buy_price. Never throws.
    /// </summary>
    private static void VerifyProjectionMatchesStoredState(SqliteConnection conn, SqliteTransaction tx)
    {
        // Skip on fresh databases (no rows)
        using (var count = conn.CreateCommand())
        {
            count.Transaction = tx;
            count.CommandText = "SELECT COUNT(*) FROM portfolio WHERE buy_price IS NOT NULL";
            var n = (long)count.ExecuteScalar()!;
            if (n == 0) return;
        }

        // Read each portfolio entry + its stored buy_price/quantity.
        // buy_price and quantity are REAL in SQLite — read as double then cast to decimal,
        // matching the convention in GetEntriesAsync / AddAsync.
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
                // Trade columns: trade_type TEXT, price REAL, quantity INTEGER, commission REAL.
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

    private static bool ColumnExists(SqliteConnection conn, SqliteTransaction tx, string table, string column)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"PRAGMA table_info({table})";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            if (string.Equals(r.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public async Task<IReadOnlyList<PortfolioEntry>> GetEntriesAsync()
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync().ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, symbol, exchange, asset_type, display_name, currency, is_active FROM portfolio ORDER BY rowid;";
        var results = new List<PortfolioEntry>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var assetType = Enum.TryParse<AssetType>(reader.GetString(3), out var t) ? t : AssetType.Stock;
            var isActive = reader.IsDBNull(6) ? true : reader.GetInt64(6) != 0;
            results.Add(new PortfolioEntry(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1),
                reader.GetString(2),
                assetType,
                reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                reader.IsDBNull(5) ? "TWD" : reader.GetString(5),
                isActive));
        }
        return results;
    }

    public async Task AddAsync(PortfolioEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync().ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR IGNORE INTO portfolio (id, symbol, exchange, asset_type, display_name, currency, created_at, updated_at, is_active)
            VALUES ($id, $sym, $ex, $at, $dn, $cur, $created_at, $updated_at, $ia);
            """;
        cmd.Parameters.AddWithValue("$id", entry.Id.ToString());
        cmd.Parameters.AddWithValue("$sym", entry.Symbol);
        cmd.Parameters.AddWithValue("$ex", entry.Exchange);
        cmd.Parameters.AddWithValue("$at", entry.AssetType.ToString());
        cmd.Parameters.AddWithValue("$dn", entry.DisplayName);
        cmd.Parameters.AddWithValue("$cur", entry.Currency);
        var now = DateTime.UtcNow.ToString("o");
        cmd.Parameters.AddWithValue("$created_at", now);
        cmd.Parameters.AddWithValue("$updated_at", now);
        cmd.Parameters.AddWithValue("$ia", entry.IsActive ? 1 : 0);
        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    public async Task UpdateAsync(PortfolioEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync().ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE portfolio SET asset_type=$at, updated_at=$updated_at
            WHERE id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", entry.Id.ToString());
        cmd.Parameters.AddWithValue("$at", entry.AssetType.ToString());
        cmd.Parameters.AddWithValue("$updated_at", DateTime.UtcNow.ToString("o"));
        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    public async Task UpdateMetadataAsync(Guid id, string displayName, string currency)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync().ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE portfolio SET display_name=$dn, currency=$cur, updated_at=$updated_at
            WHERE id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", id.ToString());
        cmd.Parameters.AddWithValue("$dn", displayName);
        cmd.Parameters.AddWithValue("$cur", currency);
        cmd.Parameters.AddWithValue("$updated_at", DateTime.UtcNow.ToString("o"));
        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    public async Task RemoveAsync(Guid id)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync().ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM portfolio WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id.ToString());
        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PortfolioEntry>> GetActiveAsync()
    {
        var all = await GetEntriesAsync().ConfigureAwait(false);
        return all.Where(e => e.IsActive).ToList();
    }

    public async Task<Guid> FindOrCreatePortfolioEntryAsync(
        string symbol, string exchange, string? displayName, AssetType assetType,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        ArgumentException.ThrowIfNullOrWhiteSpace(exchange);

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);

        // Existing?
        await using (var sel = conn.CreateCommand())
        {
            sel.CommandText = "SELECT id FROM portfolio WHERE symbol = $s AND exchange = $e LIMIT 1";
            sel.Parameters.AddWithValue("$s", symbol);
            sel.Parameters.AddWithValue("$e", exchange);
            var r = await sel.ExecuteScalarAsync(ct).ConfigureAwait(false);
            if (r is string s1) return Guid.Parse(s1);
        }

        var id = Guid.NewGuid();
        try
        {
            await using var ins = conn.CreateCommand();
            ins.CommandText = @"INSERT INTO portfolio(id, symbol, exchange, asset_type, created_at, updated_at, display_name, currency, is_active)
                                VALUES($id, $s, $e, $t, datetime('now'), datetime('now'), $dn, 'TWD', 1)";
            ins.Parameters.AddWithValue("$id", id.ToString());
            ins.Parameters.AddWithValue("$s", symbol);
            ins.Parameters.AddWithValue("$e", exchange);
            ins.Parameters.AddWithValue("$t", assetType.ToString());
            ins.Parameters.AddWithValue("$dn", (object?)displayName ?? "");
            await ins.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return id;
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19 /* SQLITE_CONSTRAINT */)
        {
            await using var sel2 = conn.CreateCommand();
            sel2.CommandText = "SELECT id FROM portfolio WHERE symbol = $s AND exchange = $e LIMIT 1";
            sel2.Parameters.AddWithValue("$s", symbol);
            sel2.Parameters.AddWithValue("$e", exchange);
            var r2 = await sel2.ExecuteScalarAsync(ct).ConfigureAwait(false);
            if (r2 is string s2) return Guid.Parse(s2);
            throw;
        }
    }

    public async Task ArchiveAsync(Guid id)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync().ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE portfolio SET is_active = 0, updated_at = datetime('now') WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id.ToString());
        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    public async Task<int> HasTradeReferencesAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var checkCmd = conn.CreateCommand();
        checkCmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='trade'";
        var tableExists = await checkCmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        if (tableExists is null or DBNull || Convert.ToInt32(tableExists, System.Globalization.CultureInfo.InvariantCulture) == 0)
            return 0;
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM trade WHERE portfolio_entry_id = $id";
        cmd.Parameters.AddWithValue("$id", id.ToString());
        var r = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return r is null or DBNull ? 0 : Convert.ToInt32(r, System.Globalization.CultureInfo.InvariantCulture);
    }
}
