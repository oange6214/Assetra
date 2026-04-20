using System.Globalization;
using Microsoft.Data.Sqlite;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;

namespace Assetra.Infrastructure.Persistence;

public sealed class TradeSqliteRepository : ITradeRepository
{
    private readonly string _connectionString;

    public TradeSqliteRepository(string dbPath)
    {
        _connectionString = $"Data Source={dbPath}";
        Initialize();
    }

    // ─── Schema bootstrap ────────────────────────────────────────────────

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

            // ── Incremental migrations (idempotent) ─────────────────────
            // Wave 1: Income / Dividend types
            MigrateAddColumn(conn, tx, "cash_amount",          "REAL");
            MigrateAddColumn(conn, tx, "cash_account_id",      "TEXT");
            MigrateAddColumn(conn, tx, "note",                 "TEXT");
            // Wave 2: PortfolioEntry link
            MigrateAddColumn(conn, tx, "portfolio_entry_id",   "TEXT");
            // Wave 3: Audit timestamps + commissions
            MigrateAddColumn(conn, tx, "created_at",           "TEXT NOT NULL DEFAULT ''");
            MigrateAddColumn(conn, tx, "updated_at",           "TEXT NOT NULL DEFAULT ''");
            MigrateAddColumn(conn, tx, "commission",           "REAL");
            MigrateAddColumn(conn, tx, "commission_discount",  "REAL");
            // Wave 4: Liability / loan fields (2026-04)
            MigrateAddColumn(conn, tx, "liability_account_id", "TEXT");
            MigrateAddColumn(conn, tx, "principal",            "REAL");
            MigrateAddColumn(conn, tx, "interest_paid",        "REAL");
            // Wave 4: Transfer field (2026-04)
            MigrateAddColumn(conn, tx, "to_cash_account_id",   "TEXT");
            // Wave 8 (2026-04): Replace liability_account_id (Guid FK) with loan_label (free text)
            MigrateAddColumn(conn, tx, "loan_label",           "TEXT");
            // Wave 9 (2026-04): Fee sub-record parent link
            MigrateAddColumn(conn, tx, "parent_trade_id",      "TEXT");

            // Indexes on FK columns — idempotent, safe to repeat
            cmd.CommandText = """
                CREATE INDEX IF NOT EXISTS idx_trade_cash_acct ON trade (cash_account_id);
                CREATE INDEX IF NOT EXISTS idx_trade_loan_label ON trade (loan_label);
                """;
            cmd.ExecuteNonQuery();

            // Wave 6 (2026-04): Backfill liability_account_id for LoanBorrow trades that were
            // recorded before the liability_account_id column existed (Wave 4). Match by name:
            // if a LoanBorrow trade's `name` exactly matches a liability_account.name, link them.
            // Idempotent — only touches rows where liability_account_id IS NULL.
            //
            // Wave 7 (2026-04-19): `liability_account` is migrated into `asset` and dropped by
            // AssetSqliteRepository. Skip this backfill when the legacy table is gone — on
            // post-Wave-7 databases the UUIDs were already copied over verbatim, so trade.
            // liability_account_id FKs remain valid without any further touching.
            if (TableExists(conn, tx, "liability_account"))
            {
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

            // Wave 8 (2026-04): Backfill loan_label from the liability name for existing
            // LoanBorrow / LoanRepay trades that were linked via liability_account_id.
            // Idempotent — only touches rows where loan_label IS NULL and
            // liability_account_id IS NOT NULL.
            //
            // Two paths depending on migration state:
            //   Pre-Wave-7:  liability_account table still exists — join it directly.
            //   Post-Wave-7: liability_account was merged into asset (same UUIDs) — fall back to asset.
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
            }
            else if (TableExists(conn, tx, "asset"))
            {
                // Post-Wave-7: liability_account was dropped; its rows (with same UUIDs) are now in asset.
                // Guard with TableExists("asset") so a fresh DB (no asset table yet) skips this branch.
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

            // Wave 5 (2026-04): TradeType enum cleanup — 'Interest' removed.
            // Legacy rows stored interest / loan fees as trade_type='Interest'
            // with a negative cash_amount (outflow). The new single-truth model
            // represents the same cash outflow as a Withdrawal with a positive
            // cash_amount (sign is applied at projection time). Convert in place
            // so Enum.Parse<TradeType> stops throwing on startup.
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

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    private static readonly HashSet<string> AllowedColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        "cash_amount", "cash_account_id", "note", "portfolio_entry_id",
        "created_at", "updated_at", "commission", "commission_discount",
        // liability_account_id: kept for Wave 4/6/8 migration backfill cross-reference;
        //   not projected into Trade model (absent from SelectClause and BindTradeParams).
        "liability_account_id", "principal", "interest_paid", "to_cash_account_id",
        "loan_label", "parent_trade_id",
    };

    private static readonly HashSet<string> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "REAL", "TEXT", "TEXT NOT NULL DEFAULT ''",
    };

    /// <summary>
    /// Idempotent helper — adds a column only if it doesn't already exist.
    /// SQLite does not support IF NOT EXISTS for ALTER TABLE ADD COLUMN.
    /// Runs inside the caller's transaction so the entire Initialize() is atomic.
    /// </summary>
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

    /// <summary>
    /// Returns true when the named table exists in the current database.
    /// Used to guard legacy migration steps that reference tables which may
    /// have been dropped by a later wave (e.g. Wave 7 drops liability_account).
    /// </summary>
    private static bool TableExists(SqliteConnection conn, SqliteTransaction tx, string tableName)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $name LIMIT 1;";
        cmd.Parameters.AddWithValue("$name", tableName);
        return cmd.ExecuteScalar() is not null;
    }

    // ─── Column ordinals (must stay in sync with every SELECT) ──────────
    //  0  id                    10  cash_amount
    //  1  symbol                11  cash_account_id
    //  2  exchange              12  note
    //  3  name                  13  portfolio_entry_id
    //  4  trade_type            14  commission
    //  5  trade_date            15  commission_discount
    //  6  price                 16  loan_label
    //  7  quantity              17  principal
    //  8  realized_pnl          18  interest_paid
    //  9  realized_pnl_pct      19  to_cash_account_id
    //                           20  parent_trade_id

    private const string SelectClause =
        "id, symbol, exchange, name, trade_type, trade_date, " +
        "price, quantity, realized_pnl, realized_pnl_pct, " +
        "cash_amount, cash_account_id, note, portfolio_entry_id, commission, " +
        "commission_discount, loan_label, principal, interest_paid, " +
        "to_cash_account_id, parent_trade_id";

    private static Trade MapTrade(SqliteDataReader r) => new(
        Id:                  Guid.Parse(r.GetString(0)),
        Symbol:              r.GetString(1),
        Exchange:            r.GetString(2),
        Name:                r.GetString(3),
        Type:                Enum.Parse<TradeType>(r.GetString(4)),
        TradeDate:           DateTime.Parse(r.GetString(5), null, DateTimeStyles.RoundtripKind),
        Price:               (decimal)r.GetDouble(6),
        Quantity:            r.GetInt32(7),
        RealizedPnl:         r.IsDBNull(8)  ? null : (decimal)r.GetDouble(8),
        RealizedPnlPct:      r.IsDBNull(9)  ? null : (decimal)r.GetDouble(9),
        CashAmount:          r.IsDBNull(10) ? null : (decimal)r.GetDouble(10),
        CashAccountId:       r.IsDBNull(11) ? null : Guid.Parse(r.GetString(11)),
        Note:                r.IsDBNull(12) ? null : r.GetString(12),
        PortfolioEntryId:    r.IsDBNull(13) ? null : Guid.Parse(r.GetString(13)),
        Commission:          r.IsDBNull(14) ? null : (decimal)r.GetDouble(14),
        CommissionDiscount:  r.IsDBNull(15) ? null : (decimal)r.GetDouble(15),
        LoanLabel:           r.IsDBNull(16) ? null : r.GetString(16),
        Principal:           r.IsDBNull(17) ? null : (decimal)r.GetDouble(17),
        InterestPaid:        r.IsDBNull(18) ? null : (decimal)r.GetDouble(18),
        ToCashAccountId:     r.IsDBNull(19) ? null : Guid.Parse(r.GetString(19)),
        ParentTradeId:       r.IsDBNull(20) ? null : Guid.Parse(r.GetString(20)));

    private static void BindTradeParams(SqliteCommand cmd, Trade t)
    {
        cmd.Parameters.AddWithValue("$id",      t.Id.ToString());
        cmd.Parameters.AddWithValue("$sym",     t.Symbol);
        cmd.Parameters.AddWithValue("$ex",      t.Exchange);
        cmd.Parameters.AddWithValue("$name",    t.Name);
        cmd.Parameters.AddWithValue("$type",    t.Type.ToString());
        cmd.Parameters.AddWithValue("$date",    t.TradeDate.ToUniversalTime().ToString("o"));
        cmd.Parameters.AddWithValue("$price",   (double)t.Price);
        cmd.Parameters.AddWithValue("$qty",     t.Quantity);
        cmd.Parameters.AddWithValue("$rpnl",    t.RealizedPnl.HasValue       ? (object)(double)t.RealizedPnl.Value            : DBNull.Value);
        cmd.Parameters.AddWithValue("$rpct",    t.RealizedPnlPct.HasValue    ? (object)(double)t.RealizedPnlPct.Value         : DBNull.Value);
        cmd.Parameters.AddWithValue("$cash",    t.CashAmount.HasValue        ? (object)(double)t.CashAmount.Value             : DBNull.Value);
        cmd.Parameters.AddWithValue("$acct",    t.CashAccountId.HasValue     ? (object)t.CashAccountId.Value.ToString()      : DBNull.Value);
        cmd.Parameters.AddWithValue("$note",    t.Note is not null           ? (object)t.Note                                : DBNull.Value);
        cmd.Parameters.AddWithValue("$pentry",  t.PortfolioEntryId.HasValue  ? (object)t.PortfolioEntryId.Value.ToString()   : DBNull.Value);
        cmd.Parameters.AddWithValue("$comm",    t.Commission.HasValue        ? (object)(double)t.Commission.Value            : DBNull.Value);
        cmd.Parameters.AddWithValue("$comm_d",  t.CommissionDiscount.HasValue? (object)(double)t.CommissionDiscount.Value    : DBNull.Value);
        cmd.Parameters.AddWithValue("$loan_label", t.LoanLabel is not null   ? (object)t.LoanLabel                          : DBNull.Value);
        cmd.Parameters.AddWithValue("$princ",   t.Principal.HasValue         ? (object)(double)t.Principal.Value             : DBNull.Value);
        cmd.Parameters.AddWithValue("$int",     t.InterestPaid.HasValue      ? (object)(double)t.InterestPaid.Value          : DBNull.Value);
        cmd.Parameters.AddWithValue("$to_acct",    t.ToCashAccountId.HasValue  ? (object)t.ToCashAccountId.Value.ToString()  : DBNull.Value);
        cmd.Parameters.AddWithValue("$parent_id",  t.ParentTradeId.HasValue    ? (object)t.ParentTradeId.Value.ToString()    : DBNull.Value);
    }

    // ─── Queries ─────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<Trade>> GetAllAsync()
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync().ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {SelectClause} FROM trade ORDER BY trade_date DESC, rowid DESC;";
        var results = new List<Trade>();
        await using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
        while (await reader.ReadAsync().ConfigureAwait(false))
            results.Add(MapTrade(reader));
        return results;
    }

    public async Task<IReadOnlyList<Trade>> GetByCashAccountAsync(Guid cashAccountId)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync().ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        // Include Transfer records where this account is the destination
        cmd.CommandText =
            $"SELECT {SelectClause} FROM trade " +
            "WHERE cash_account_id = $acct OR to_cash_account_id = $acct " +
            "ORDER BY trade_date DESC, rowid DESC;";
        cmd.Parameters.AddWithValue("$acct", cashAccountId.ToString());
        var results = new List<Trade>();
        await using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
        while (await reader.ReadAsync().ConfigureAwait(false))
            results.Add(MapTrade(reader));
        return results;
    }

    public async Task<IReadOnlyList<Trade>> GetByLoanLabelAsync(string loanLabel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(loanLabel);
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync().ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            $"SELECT {SelectClause} FROM trade " +
            "WHERE loan_label = $loan_label " +
            "ORDER BY trade_date DESC, rowid DESC;";
        cmd.Parameters.AddWithValue("$loan_label", loanLabel);
        var results = new List<Trade>();
        await using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
        while (await reader.ReadAsync().ConfigureAwait(false))
            results.Add(MapTrade(reader));
        return results;
    }

    // ─── Mutations ───────────────────────────────────────────────────────

    public async Task AddAsync(Trade trade)
    {
        ArgumentNullException.ThrowIfNull(trade);
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync().ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR IGNORE INTO trade
                (id, symbol, exchange, name, trade_type, trade_date, price, quantity,
                 realized_pnl, realized_pnl_pct, cash_amount, cash_account_id, note,
                 portfolio_entry_id, commission, commission_discount,
                 loan_label, principal, interest_paid, to_cash_account_id,
                 parent_trade_id, created_at, updated_at)
            VALUES
                ($id, $sym, $ex, $name, $type, $date, $price, $qty,
                 $rpnl, $rpct, $cash, $acct, $note,
                 $pentry, $comm, $comm_d,
                 $loan_label, $princ, $int, $to_acct,
                 $parent_id, $created_at, $updated_at);
            """;
        BindTradeParams(cmd, trade);
        var now = DateTime.UtcNow.ToString("o");
        cmd.Parameters.AddWithValue("$created_at", now);
        cmd.Parameters.AddWithValue("$updated_at", now);
        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    public async Task UpdateAsync(Trade trade)
    {
        ArgumentNullException.ThrowIfNull(trade);
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync().ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE trade SET
                symbol = $sym, exchange = $ex, name = $name, trade_type = $type,
                trade_date = $date, price = $price, quantity = $qty,
                realized_pnl = $rpnl, realized_pnl_pct = $rpct,
                cash_amount = $cash, cash_account_id = $acct, note = $note,
                portfolio_entry_id = $pentry, commission = $comm,
                commission_discount = $comm_d,
                loan_label = $loan_label, principal = $princ,
                interest_paid = $int, to_cash_account_id = $to_acct,
                parent_trade_id = $parent_id, updated_at = $updated_at
            WHERE id = $id;
            """;
        BindTradeParams(cmd, trade);
        cmd.Parameters.AddWithValue("$updated_at", DateTime.UtcNow.ToString("o"));
        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    public async Task RemoveAsync(Guid id)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync().ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM trade WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id.ToString());
        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    public async Task RemoveChildrenAsync(Guid parentId)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync().ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM trade WHERE parent_trade_id = $pid;";
        cmd.Parameters.AddWithValue("$pid", parentId.ToString());
        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }
}
