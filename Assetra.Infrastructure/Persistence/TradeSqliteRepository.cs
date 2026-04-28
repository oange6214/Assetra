using System.Globalization;
using Microsoft.Data.Sqlite;
using Assetra.Core.Interfaces;
using Assetra.Core.Interfaces.Sync;
using Assetra.Core.Models;
using Assetra.Core.Models.Sync;
using Assetra.Infrastructure.Sync;

namespace Assetra.Infrastructure.Persistence;

/// <summary>
/// SQLite-backed <see cref="ITradeRepository"/>，同時實作 <see cref="ITradeSyncStore"/>。
/// 鏡 <see cref="CategorySqliteRepository"/> 的 sync 模式：
/// <list type="bullet">
///   <item><see cref="AddAsync"/>：寫入時 stamp version=1、last_modified、is_pending_push=1</item>
///   <item><see cref="UpdateAsync"/>：bump version、刷新 last_modified、is_pending_push=1</item>
///   <item><see cref="RemoveAsync"/>：soft delete（保留 row、is_deleted=1、bump version）；同時把
///         children 的 parent_trade_id 設 NULL，並將指向被刪 row 的 cash_account_id 等不另外處理。</item>
///   <item>Bulk cascade 路徑（<see cref="RemoveChildrenAsync"/> / <see cref="RemoveByAccountIdAsync"/> /
///         <see cref="RemoveByLiabilityAsync"/>）v0.20.8 起改為 soft delete，
///         與 <see cref="RemoveAsync"/> 對齊；cloud 會收到每筆被連動刪除的 trade tombstone。</item>
/// </list>
/// </summary>
public sealed class TradeSqliteRepository : ITradeRepository, ITradeSyncStore
{
    private readonly string _connectionString;
    private readonly string _deviceId;
    private readonly TimeProvider _time;

    public TradeSqliteRepository(string dbPath, string deviceId = "local", TimeProvider? time = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(deviceId);
        _connectionString = $"Data Source={dbPath}";
        _deviceId = deviceId;
        _time = time ?? TimeProvider.System;
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
    //                           22  category_id
    //                           23  recurring_source_id

    private const string SelectClause =
        "id, symbol, exchange, name, trade_type, trade_date, " +
        "price, quantity, realized_pnl, realized_pnl_pct, " +
        "cash_amount, cash_account_id, note, portfolio_entry_id, commission, " +
        "commission_discount, loan_label, principal, interest_paid, " +
        "to_cash_account_id, liability_asset_id, parent_trade_id, " +
        "category_id, recurring_source_id";

    private const string SyncSelectClause = SelectClause +
        ", version, last_modified_at, last_modified_by_device, is_deleted";

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
        ParentTradeId: r.IsDBNull(21) ? null : Guid.Parse(r.GetString(21)),
        CategoryId: r.IsDBNull(22) ? null : Guid.Parse(r.GetString(22)),
        RecurringSourceId: r.IsDBNull(23) ? null : Guid.Parse(r.GetString(23)));

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
        cmd.Parameters.AddWithValue("$category_id", t.CategoryId.HasValue ? (object)t.CategoryId.Value.ToString() : DBNull.Value);
        cmd.Parameters.AddWithValue("$recurring_source_id", t.RecurringSourceId.HasValue ? (object)t.RecurringSourceId.Value.ToString() : DBNull.Value);
    }

    // ─── Queries (filter is_deleted = 0) ─────────────────────────────────

    public async Task<IReadOnlyList<Trade>> GetAllAsync(CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {SelectClause} FROM trade WHERE is_deleted = 0 ORDER BY trade_date DESC, rowid DESC;";
        var results = new List<Trade>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            results.Add(MapTrade(reader));
        return results;
    }

    public async Task<IReadOnlyList<Trade>> GetByPortfolioEntryIdsAsync(
        IReadOnlyCollection<Guid> entryIds, CancellationToken ct = default)
    {
        if (entryIds.Count == 0) return Array.Empty<Trade>();
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        var idList = entryIds.Distinct().ToList();
        var placeholders = string.Join(",", idList.Select((_, i) => $"$p{i}"));
        cmd.CommandText = $"SELECT {SelectClause} FROM trade WHERE is_deleted = 0 AND portfolio_entry_id IN ({placeholders}) ORDER BY trade_date DESC, rowid DESC;";
        for (int i = 0; i < idList.Count; i++)
            cmd.Parameters.AddWithValue($"$p{i}", idList[i].ToString());
        var results = new List<Trade>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            results.Add(MapTrade(reader));
        return results;
    }

    public async Task<IReadOnlyList<Trade>> GetByPeriodAsync(DateTime from, DateTime to, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {SelectClause} FROM trade WHERE is_deleted = 0 AND trade_date >= $from AND trade_date <= $to ORDER BY trade_date DESC, rowid DESC;";
        cmd.Parameters.AddWithValue("$from", from.ToUniversalTime().ToString("o"));
        cmd.Parameters.AddWithValue("$to", to.ToUniversalTime().ToString("o"));
        var results = new List<Trade>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            results.Add(MapTrade(reader));
        return results;
    }

    public async Task<Trade?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {SelectClause} FROM trade WHERE id = $id AND is_deleted = 0;";
        cmd.Parameters.AddWithValue("$id", id.ToString());
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (await reader.ReadAsync(ct).ConfigureAwait(false))
            return MapTrade(reader);
        return null;
    }

    public async Task<IReadOnlyList<Trade>> GetByCashAccountAsync(Guid cashAccountId, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            $"SELECT {SelectClause} FROM trade " +
            "WHERE is_deleted = 0 AND (cash_account_id = $acct OR to_cash_account_id = $acct) " +
            "ORDER BY trade_date DESC, rowid DESC;";
        cmd.Parameters.AddWithValue("$acct", cashAccountId.ToString());
        var results = new List<Trade>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            results.Add(MapTrade(reader));
        return results;
    }

    public async Task<IReadOnlyList<Trade>> GetByLoanLabelAsync(string loanLabel, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(loanLabel);
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            $"SELECT {SelectClause} FROM trade " +
            "WHERE is_deleted = 0 AND loan_label = $loan_label " +
            "ORDER BY trade_date DESC, rowid DESC;";
        cmd.Parameters.AddWithValue("$loan_label", loanLabel);
        var results = new List<Trade>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            results.Add(MapTrade(reader));
        return results;
    }

    // ─── Mutations ───────────────────────────────────────────────────────

    public async Task AddAsync(Trade trade, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(trade);
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = InsertSql;
        BindTradeParams(cmd, trade);
        var now = _time.GetUtcNow().UtcDateTime.ToString("o");
        cmd.Parameters.AddWithValue("$now", now);
        cmd.Parameters.AddWithValue("$device", _deviceId);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task UpdateAsync(Trade trade, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(trade);
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
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
                parent_trade_id = $parent_id,
                category_id = $category_id,
                recurring_source_id = $recurring_source_id,
                updated_at = $now,
                version = version + 1,
                last_modified_at = $now,
                last_modified_by_device = $device,
                is_pending_push = 1
            WHERE id = $id;
            """;
        BindTradeParams(cmd, trade);
        cmd.Parameters.AddWithValue("$now", _time.GetUtcNow().UtcDateTime.ToString("o"));
        cmd.Parameters.AddWithValue("$device", _deviceId);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task RemoveAsync(Guid id, CancellationToken ct = default)
    {
        // Soft delete (tombstone): keep row so deletion can be pushed to remote.
        // Detach children (set parent_trade_id = NULL) so dangling FKs don't break UI.
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct).ConfigureAwait(false);

        await using (var detach = conn.CreateCommand())
        {
            detach.Transaction = tx;
            detach.CommandText = "UPDATE trade SET parent_trade_id = NULL WHERE parent_trade_id = $id;";
            detach.Parameters.AddWithValue("$id", id.ToString());
            await detach.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await using (var del = conn.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = """
                UPDATE trade SET
                    is_deleted = 1,
                    version = version + 1,
                    updated_at = $now,
                    last_modified_at = $now,
                    last_modified_by_device = $device,
                    is_pending_push = 1
                WHERE id = $id;
                """;
            del.Parameters.AddWithValue("$id", id.ToString());
            del.Parameters.AddWithValue("$now", _time.GetUtcNow().UtcDateTime.ToString("o"));
            del.Parameters.AddWithValue("$device", _deviceId);
            await del.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await tx.CommitAsync(ct).ConfigureAwait(false);
    }

    // v0.20.8: cascade bulk deletes converted to soft delete so cloud receives tombstone events.
    public async Task RemoveChildrenAsync(Guid parentId, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE trade SET
                is_deleted = 1,
                version = version + 1,
                updated_at = $now,
                last_modified_at = $now,
                last_modified_by_device = $device,
                is_pending_push = 1
            WHERE parent_trade_id = $pid AND is_deleted = 0;
            """;
        cmd.Parameters.AddWithValue("$pid", parentId.ToString());
        cmd.Parameters.AddWithValue("$now", _time.GetUtcNow().UtcDateTime.ToString("o"));
        cmd.Parameters.AddWithValue("$device", _deviceId);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task RemoveByAccountIdAsync(Guid accountId, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE trade SET
                is_deleted = 1,
                version = version + 1,
                updated_at = $now,
                last_modified_at = $now,
                last_modified_by_device = $device,
                is_pending_push = 1
            WHERE is_deleted = 0 AND (cash_account_id = $id OR to_cash_account_id = $id);
            """;
        cmd.Parameters.AddWithValue("$id", accountId.ToString());
        cmd.Parameters.AddWithValue("$now", _time.GetUtcNow().UtcDateTime.ToString("o"));
        cmd.Parameters.AddWithValue("$device", _deviceId);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task RemoveByLiabilityAsync(Guid? liabilityAssetId, string? loanLabel, CancellationToken ct = default)
    {
        if (!liabilityAssetId.HasValue && string.IsNullOrEmpty(loanLabel))
            return;

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();

        var clauses = new List<string>();
        if (liabilityAssetId.HasValue)
        {
            clauses.Add("liability_asset_id = $aid");
            cmd.Parameters.AddWithValue("$aid", liabilityAssetId.Value.ToString());
        }
        if (!string.IsNullOrEmpty(loanLabel))
        {
            clauses.Add("loan_label = $label");
            cmd.Parameters.AddWithValue("$label", loanLabel);
        }

        cmd.CommandText =
            "UPDATE trade SET " +
            "    is_deleted = 1, " +
            "    version = version + 1, " +
            "    updated_at = $now, " +
            "    last_modified_at = $now, " +
            "    last_modified_by_device = $device, " +
            "    is_pending_push = 1 " +
            "WHERE is_deleted = 0 AND (" + string.Join(" OR ", clauses) + ");";
        cmd.Parameters.AddWithValue("$now", _time.GetUtcNow().UtcDateTime.ToString("o"));
        cmd.Parameters.AddWithValue("$device", _deviceId);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task ApplyAtomicAsync(IReadOnlyList<TradeMutation> mutations, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(mutations);
        if (mutations.Count == 0) return;

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct).ConfigureAwait(false);

        try
        {
            foreach (var m in mutations)
            {
                ct.ThrowIfCancellationRequested();
                switch (m)
                {
                    case AddTradeMutation add:
                        await ExecAddAsync(conn, tx, add.Trade, ct).ConfigureAwait(false);
                        break;
                    case RemoveTradeMutation rem:
                        await ExecRemoveAsync(conn, tx, rem.Id, ct).ConfigureAwait(false);
                        break;
                    default:
                        throw new InvalidOperationException($"Unknown mutation type: {m.GetType().Name}");
                }
            }
            await tx.CommitAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            await tx.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private async Task ExecAddAsync(SqliteConnection conn, SqliteTransaction tx, Trade trade, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(trade);
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = InsertSql;
        BindTradeParams(cmd, trade);
        var now = _time.GetUtcNow().UtcDateTime.ToString("o");
        cmd.Parameters.AddWithValue("$now", now);
        cmd.Parameters.AddWithValue("$device", _deviceId);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task ExecRemoveAsync(SqliteConnection conn, SqliteTransaction tx, Guid id, CancellationToken ct)
    {
        await using (var detach = conn.CreateCommand())
        {
            detach.Transaction = tx;
            detach.CommandText = "UPDATE trade SET parent_trade_id = NULL WHERE parent_trade_id = $id;";
            detach.Parameters.AddWithValue("$id", id.ToString());
            await detach.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            UPDATE trade SET
                is_deleted = 1,
                version = version + 1,
                updated_at = $now,
                last_modified_at = $now,
                last_modified_by_device = $device,
                is_pending_push = 1
            WHERE id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", id.ToString());
        cmd.Parameters.AddWithValue("$now", _time.GetUtcNow().UtcDateTime.ToString("o"));
        cmd.Parameters.AddWithValue("$device", _deviceId);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private const string InsertSql = """
        INSERT OR IGNORE INTO trade
            (id, symbol, exchange, name, trade_type, trade_date, price, quantity,
             realized_pnl, realized_pnl_pct, cash_amount, cash_account_id, note,
             portfolio_entry_id, commission, commission_discount,
             loan_label, principal, interest_paid, to_cash_account_id,
             liability_asset_id,
             parent_trade_id, category_id, recurring_source_id,
             created_at, updated_at,
             version, last_modified_at, last_modified_by_device, is_deleted, is_pending_push)
        VALUES
            ($id, $sym, $ex, $name, $type, $date, $price, $qty,
             $rpnl, $rpct, $cash, $acct, $note,
             $pentry, $comm, $comm_d,
             $loan_label, $princ, $int, $to_acct,
             $liability_asset_id,
             $parent_id, $category_id, $recurring_source_id,
             $now, $now,
             1, $now, $device, 0, 1);
        """;

    // ── ITradeSyncStore ─────────────────────────────────────────────────────

    public async Task<IReadOnlyList<SyncEnvelope>> GetPendingPushAsync(CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {SyncSelectClause} FROM trade WHERE is_pending_push = 1;";
        var results = new List<SyncEnvelope>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var trade = MapTrade(reader);
            var version = new EntityVersion(
                Version: reader.GetInt64(24),
                LastModifiedAt: DateTimeOffset.Parse(reader.GetString(25)),
                LastModifiedByDevice: reader.GetString(26));
            var isDeleted = reader.GetInt32(27) != 0;
            results.Add(TradeSyncMapper.ToEnvelope(trade, version, isDeleted));
        }
        return results;
    }

    public async Task MarkPushedAsync(IReadOnlyList<Guid> ids, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.Count == 0) return;
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "UPDATE trade SET is_pending_push = 0 WHERE id = $id;";
        var p = cmd.Parameters.Add("$id", SqliteType.Text);
        foreach (var id in ids)
        {
            p.Value = id.ToString();
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        await tx.CommitAsync(ct).ConfigureAwait(false);
    }

    public async Task ApplyRemoteAsync(IReadOnlyList<SyncEnvelope> envelopes, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(envelopes);
        if (envelopes.Count == 0) return;
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct).ConfigureAwait(false);

        foreach (var env in envelopes)
        {
            if (env.EntityType != TradeSyncMapper.EntityType) continue;

            await using (var probe = conn.CreateCommand())
            {
                probe.Transaction = tx;
                probe.CommandText = "SELECT version FROM trade WHERE id = $id;";
                probe.Parameters.AddWithValue("$id", env.EntityId.ToString());
                var existing = await probe.ExecuteScalarAsync(ct).ConfigureAwait(false);
                if (existing is not null && Convert.ToInt64(existing) >= env.Version.Version)
                    continue; // never write backwards
            }

            if (env.Deleted)
            {
                // Tombstone insert: provide minimum NOT NULL placeholders for new rows.
                await using var del = conn.CreateCommand();
                del.Transaction = tx;
                del.CommandText = """
                    INSERT INTO trade
                        (id, symbol, exchange, name, trade_type, trade_date, price, quantity,
                         created_at, updated_at,
                         version, last_modified_at, last_modified_by_device, is_deleted, is_pending_push)
                    VALUES
                        ($id, '', '', '', 'Buy', $now, 0, 0,
                         $now, $now,
                         $ver, $modAt, $modBy, 1, 0)
                    ON CONFLICT(id) DO UPDATE SET
                        is_deleted = 1,
                        version = excluded.version,
                        last_modified_at = excluded.last_modified_at,
                        last_modified_by_device = excluded.last_modified_by_device,
                        updated_at = excluded.updated_at,
                        is_pending_push = 0;
                    """;
                del.Parameters.AddWithValue("$id", env.EntityId.ToString());
                del.Parameters.AddWithValue("$now", _time.GetUtcNow().UtcDateTime.ToString("o"));
                del.Parameters.AddWithValue("$ver", env.Version.Version);
                del.Parameters.AddWithValue("$modAt", env.Version.LastModifiedAt.UtcDateTime.ToString("o"));
                del.Parameters.AddWithValue("$modBy", env.Version.LastModifiedByDevice);
                await del.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                continue;
            }

            var t = TradeSyncMapper.FromPayload(env);
            await using var up = conn.CreateCommand();
            up.Transaction = tx;
            up.CommandText = """
                INSERT INTO trade
                    (id, symbol, exchange, name, trade_type, trade_date, price, quantity,
                     realized_pnl, realized_pnl_pct, cash_amount, cash_account_id, note,
                     portfolio_entry_id, commission, commission_discount,
                     loan_label, principal, interest_paid, to_cash_account_id,
                     liability_asset_id,
                     parent_trade_id, category_id, recurring_source_id,
                     created_at, updated_at,
                     version, last_modified_at, last_modified_by_device, is_deleted, is_pending_push)
                VALUES
                    ($id, $sym, $ex, $name, $type, $date, $price, $qty,
                     $rpnl, $rpct, $cash, $acct, $note,
                     $pentry, $comm, $comm_d,
                     $loan_label, $princ, $int, $to_acct,
                     $liability_asset_id,
                     $parent_id, $category_id, $recurring_source_id,
                     $now, $now,
                     $ver, $modAt, $modBy, 0, 0)
                ON CONFLICT(id) DO UPDATE SET
                    symbol = excluded.symbol,
                    exchange = excluded.exchange,
                    name = excluded.name,
                    trade_type = excluded.trade_type,
                    trade_date = excluded.trade_date,
                    price = excluded.price,
                    quantity = excluded.quantity,
                    realized_pnl = excluded.realized_pnl,
                    realized_pnl_pct = excluded.realized_pnl_pct,
                    cash_amount = excluded.cash_amount,
                    cash_account_id = excluded.cash_account_id,
                    note = excluded.note,
                    portfolio_entry_id = excluded.portfolio_entry_id,
                    commission = excluded.commission,
                    commission_discount = excluded.commission_discount,
                    loan_label = excluded.loan_label,
                    principal = excluded.principal,
                    interest_paid = excluded.interest_paid,
                    to_cash_account_id = excluded.to_cash_account_id,
                    liability_asset_id = excluded.liability_asset_id,
                    parent_trade_id = excluded.parent_trade_id,
                    category_id = excluded.category_id,
                    recurring_source_id = excluded.recurring_source_id,
                    updated_at = excluded.updated_at,
                    version = excluded.version,
                    last_modified_at = excluded.last_modified_at,
                    last_modified_by_device = excluded.last_modified_by_device,
                    is_deleted = 0,
                    is_pending_push = 0;
                """;
            BindTradeParams(up, t);
            up.Parameters.AddWithValue("$now", _time.GetUtcNow().UtcDateTime.ToString("o"));
            up.Parameters.AddWithValue("$ver", env.Version.Version);
            up.Parameters.AddWithValue("$modAt", env.Version.LastModifiedAt.UtcDateTime.ToString("o"));
            up.Parameters.AddWithValue("$modBy", env.Version.LastModifiedByDevice);
            await up.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await tx.CommitAsync(ct).ConfigureAwait(false);
    }
}
