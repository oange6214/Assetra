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
        TradeSchemaMigrator.EnsureInitialized(_connectionString);
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
    //                           20  liability_asset_id
    //                           21  parent_trade_id

    private const string SelectClause =
        "id, symbol, exchange, name, trade_type, trade_date, " +
        "price, quantity, realized_pnl, realized_pnl_pct, " +
        "cash_amount, cash_account_id, note, portfolio_entry_id, commission, " +
        "commission_discount, loan_label, principal, interest_paid, " +
        "to_cash_account_id, liability_asset_id, parent_trade_id";

    private static Trade MapTrade(SqliteDataReader r) => new(
        Id: Guid.Parse(r.GetString(0)),
        Symbol: r.GetString(1),
        Exchange: r.GetString(2),
        Name: r.GetString(3),
        Type: Enum.Parse<TradeType>(r.GetString(4)),
        TradeDate: DateTime.Parse(r.GetString(5), null, DateTimeStyles.RoundtripKind),
        Price: (decimal)r.GetDouble(6),
        Quantity: r.GetInt32(7),
        RealizedPnl: r.IsDBNull(8) ? null : (decimal)r.GetDouble(8),
        RealizedPnlPct: r.IsDBNull(9) ? null : (decimal)r.GetDouble(9),
        CashAmount: r.IsDBNull(10) ? null : (decimal)r.GetDouble(10),
        CashAccountId: r.IsDBNull(11) ? null : Guid.Parse(r.GetString(11)),
        Note: r.IsDBNull(12) ? null : r.GetString(12),
        PortfolioEntryId: r.IsDBNull(13) ? null : Guid.Parse(r.GetString(13)),
        Commission: r.IsDBNull(14) ? null : (decimal)r.GetDouble(14),
        CommissionDiscount: r.IsDBNull(15) ? null : (decimal)r.GetDouble(15),
        LoanLabel: r.IsDBNull(16) ? null : r.GetString(16),
        Principal: r.IsDBNull(17) ? null : (decimal)r.GetDouble(17),
        InterestPaid: r.IsDBNull(18) ? null : (decimal)r.GetDouble(18),
        ToCashAccountId: r.IsDBNull(19) ? null : Guid.Parse(r.GetString(19)),
        LiabilityAssetId: r.IsDBNull(20) ? null : Guid.Parse(r.GetString(20)),
        ParentTradeId: r.IsDBNull(21) ? null : Guid.Parse(r.GetString(21)));

    private static void BindTradeParams(SqliteCommand cmd, Trade t)
    {
        cmd.Parameters.AddWithValue("$id", t.Id.ToString());
        cmd.Parameters.AddWithValue("$sym", t.Symbol);
        cmd.Parameters.AddWithValue("$ex", t.Exchange);
        cmd.Parameters.AddWithValue("$name", t.Name);
        cmd.Parameters.AddWithValue("$type", t.Type.ToString());
        cmd.Parameters.AddWithValue("$date", t.TradeDate.ToUniversalTime().ToString("o"));
        cmd.Parameters.AddWithValue("$price", (double)t.Price);
        cmd.Parameters.AddWithValue("$qty", t.Quantity);
        cmd.Parameters.AddWithValue("$rpnl", t.RealizedPnl.HasValue ? (object)(double)t.RealizedPnl.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("$rpct", t.RealizedPnlPct.HasValue ? (object)(double)t.RealizedPnlPct.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("$cash", t.CashAmount.HasValue ? (object)(double)t.CashAmount.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("$acct", t.CashAccountId.HasValue ? (object)t.CashAccountId.Value.ToString() : DBNull.Value);
        cmd.Parameters.AddWithValue("$note", t.Note is not null ? (object)t.Note : DBNull.Value);
        cmd.Parameters.AddWithValue("$pentry", t.PortfolioEntryId.HasValue ? (object)t.PortfolioEntryId.Value.ToString() : DBNull.Value);
        cmd.Parameters.AddWithValue("$comm", t.Commission.HasValue ? (object)(double)t.Commission.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("$comm_d", t.CommissionDiscount.HasValue ? (object)(double)t.CommissionDiscount.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("$loan_label", t.LoanLabel is not null ? (object)t.LoanLabel : DBNull.Value);
        cmd.Parameters.AddWithValue("$princ", t.Principal.HasValue ? (object)(double)t.Principal.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("$int", t.InterestPaid.HasValue ? (object)(double)t.InterestPaid.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("$to_acct", t.ToCashAccountId.HasValue ? (object)t.ToCashAccountId.Value.ToString() : DBNull.Value);
        cmd.Parameters.AddWithValue("$liability_asset_id", t.LiabilityAssetId.HasValue ? (object)t.LiabilityAssetId.Value.ToString() : DBNull.Value);
        cmd.Parameters.AddWithValue("$parent_id", t.ParentTradeId.HasValue ? (object)t.ParentTradeId.Value.ToString() : DBNull.Value);
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
                 liability_asset_id,
                 parent_trade_id, created_at, updated_at)
            VALUES
                ($id, $sym, $ex, $name, $type, $date, $price, $qty,
                 $rpnl, $rpct, $cash, $acct, $note,
                 $pentry, $comm, $comm_d,
                 $loan_label, $princ, $int, $to_acct,
                 $liability_asset_id,
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
                liability_asset_id = $liability_asset_id,
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
