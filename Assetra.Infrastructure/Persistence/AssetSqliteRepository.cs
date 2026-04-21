using Microsoft.Data.Sqlite;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;

namespace Assetra.Infrastructure.Persistence;

/// <summary>
/// SQLite implementation of <see cref="IAssetRepository"/>.
///
/// <para>
/// Wave 7 migration (2026-04): creates asset_group / asset / asset_event tables,
/// seeds system default groups, migrates existing cash_account and
/// liability_account rows (preserving their UUIDs), then drops the old tables.
/// Because the old UUIDs are preserved, Trade.cash_account_id and
/// Trade.liability_account_id foreign-key values remain valid with no changes
/// to the trade table.
/// </para>
/// </summary>
public sealed class AssetSqliteRepository : IAssetRepository
{
    private readonly string _connectionString;

    // Fixed UUIDs for system groups — deterministic so INSERT OR IGNORE is idempotent
    private static readonly Guid GrpBankAccount = new("11111111-1111-1111-1111-111111111101");
    private static readonly Guid GrpRealEstate   = new("11111111-1111-1111-1111-111111111102");
    private static readonly Guid GrpVehicle      = new("11111111-1111-1111-1111-111111111103");
    private static readonly Guid GrpBankLoan     = new("11111111-1111-1111-1111-111111111201");
    private static readonly Guid GrpCreditCard   = new("11111111-1111-1111-1111-111111111202");

    private static readonly HashSet<string> AssetAllowedColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        "is_active", "updated_at",
        "loan_annual_rate", "loan_term_months", "loan_start_date", "loan_handling_fee",
    };

    private static readonly HashSet<string> AssetAllowedTypeDefs = new(StringComparer.OrdinalIgnoreCase)
    {
        "INTEGER NOT NULL DEFAULT 1",
        "TEXT NOT NULL DEFAULT ''",
        "REAL",
        "INTEGER",
        "TEXT",
    };

    public AssetSqliteRepository(string dbPath)
    {
        _connectionString = $"Data Source={dbPath}";
        Initialize();
    }

    // ─── Schema + Wave 7 Migration ────────────────────────────────────────

    private void Initialize()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var tx = conn.BeginTransaction();
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;

            // 1. Create new tables (idempotent)
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS asset_group (
                    id           TEXT PRIMARY KEY,
                    name         TEXT NOT NULL,
                    asset_type   TEXT NOT NULL,
                    icon         TEXT,
                    sort_order   INTEGER NOT NULL DEFAULT 0,
                    is_system    INTEGER NOT NULL DEFAULT 0,
                    created_date TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS asset (
                    id           TEXT PRIMARY KEY,
                    name         TEXT NOT NULL,
                    asset_type   TEXT NOT NULL,
                    group_id     TEXT REFERENCES asset_group(id),
                    currency     TEXT NOT NULL DEFAULT 'TWD',
                    created_date TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS asset_event (
                    id               TEXT PRIMARY KEY,
                    asset_id         TEXT NOT NULL REFERENCES asset(id),
                    event_type       TEXT NOT NULL,
                    event_date       TEXT NOT NULL,
                    amount           REAL,
                    quantity         REAL,
                    note             TEXT,
                    cash_account_id  TEXT,
                    created_at       TEXT NOT NULL DEFAULT ''
                );

                CREATE INDEX IF NOT EXISTS idx_asset_type     ON asset (asset_type);
                CREATE INDEX IF NOT EXISTS idx_asset_event_id ON asset_event (asset_id);
                """;
            cmd.ExecuteNonQuery();

            // 2. Seed system groups (idempotent via INSERT OR IGNORE)
            cmd.CommandText = """
                INSERT OR IGNORE INTO asset_group (id, name, asset_type, icon, sort_order, is_system, created_date) VALUES
                    ($g1, '銀行帳戶', 'Asset',     '🏦', 0, 1, '2026-01-01'),
                    ($g2, '不動產',   'Asset',     '🏠', 1, 1, '2026-01-01'),
                    ($g3, '交通工具', 'Asset',     '🚗', 2, 1, '2026-01-01'),
                    ($g4, '銀行貸款', 'Liability', '🏦', 0, 1, '2026-01-01'),
                    ($g5, '信用卡',   'Liability', '💳', 1, 1, '2026-01-01');
                """;
            cmd.Parameters.AddWithValue("$g1", GrpBankAccount.ToString());
            cmd.Parameters.AddWithValue("$g2", GrpRealEstate.ToString());
            cmd.Parameters.AddWithValue("$g3", GrpVehicle.ToString());
            cmd.Parameters.AddWithValue("$g4", GrpBankLoan.ToString());
            cmd.Parameters.AddWithValue("$g5", GrpCreditCard.ToString());
            cmd.ExecuteNonQuery();
            cmd.Parameters.Clear();

            // Wave 9.1 — additive metadata columns
            SqliteSchemaHelper.MigrateAddColumn(conn, tx, "asset",
                "is_active", "INTEGER NOT NULL DEFAULT 1",
                AssetAllowedColumns, AssetAllowedTypeDefs);
            SqliteSchemaHelper.MigrateAddColumn(conn, tx, "asset",
                "updated_at", "TEXT NOT NULL DEFAULT ''",
                AssetAllowedColumns, AssetAllowedTypeDefs);

            // Wave 10 — loan amortization metadata (nullable; NULL = not a loan)
            SqliteSchemaHelper.MigrateAddColumn(conn, tx, "asset",
                "loan_annual_rate", "REAL", AssetAllowedColumns, AssetAllowedTypeDefs);
            SqliteSchemaHelper.MigrateAddColumn(conn, tx, "asset",
                "loan_term_months", "INTEGER", AssetAllowedColumns, AssetAllowedTypeDefs);
            SqliteSchemaHelper.MigrateAddColumn(conn, tx, "asset",
                "loan_start_date", "TEXT", AssetAllowedColumns, AssetAllowedTypeDefs);
            SqliteSchemaHelper.MigrateAddColumn(conn, tx, "asset",
                "loan_handling_fee", "REAL", AssetAllowedColumns, AssetAllowedTypeDefs);

            using (var idx = conn.CreateCommand())
            {
                idx.Transaction = tx;
                idx.CommandText = @"CREATE UNIQUE INDEX IF NOT EXISTS idx_asset_name_currency_asset
                                    ON asset(name, currency) WHERE asset_type = 'Asset'";
                idx.ExecuteNonQuery();
            }

            // 3. Wave 7: migrate cash_account → asset (if old table exists)
            if (TableExists(conn, tx, "cash_account"))
            {
                cmd.CommandText = $"""
                    INSERT OR IGNORE INTO asset (id, name, asset_type, group_id, currency, created_date)
                    SELECT id, name, 'Asset', '{GrpBankAccount}', COALESCE(currency, 'TWD'), created_date
                    FROM cash_account;
                    """;
                cmd.ExecuteNonQuery();

                cmd.CommandText = "DROP TABLE IF EXISTS cash_account;";
                cmd.ExecuteNonQuery();
            }

            // 4. Wave 7: migrate liability_account → asset (if old table exists)
            if (TableExists(conn, tx, "liability_account"))
            {
                cmd.CommandText = $"""
                    INSERT OR IGNORE INTO asset (id, name, asset_type, group_id, currency, created_date)
                    SELECT id, name, 'Liability', '{GrpBankLoan}', COALESCE(currency, 'TWD'),
                           COALESCE(created_date, date('now'))
                    FROM liability_account;
                    """;
                cmd.ExecuteNonQuery();

                cmd.CommandText = "DROP TABLE IF EXISTS liability_account;";
                cmd.ExecuteNonQuery();
            }

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    private static bool TableExists(SqliteConnection conn, SqliteTransaction tx, string table)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$t;";
        cmd.Parameters.AddWithValue("$t", table);
        return (long)(cmd.ExecuteScalar() ?? 0L) > 0;
    }

    // ─── Groups ───────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<AssetGroup>> GetGroupsAsync()
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync().ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT id, name, asset_type, icon, sort_order, is_system, created_date " +
            "FROM asset_group ORDER BY asset_type, sort_order, rowid;";
        var results = new List<AssetGroup>();
        await using var r = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
        while (await r.ReadAsync().ConfigureAwait(false))
            results.Add(MapGroup(r));
        return results;
    }

    public async Task AddGroupAsync(AssetGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync().ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR IGNORE INTO asset_group (id, name, asset_type, icon, sort_order, is_system, created_date)
            VALUES ($id, $name, $type, $icon, $sort, $sys, $dt);
            """;
        BindGroup(cmd, group);
        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    public async Task UpdateGroupAsync(AssetGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync().ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "UPDATE asset_group SET name=$name, asset_type=$type, icon=$icon, " +
            "sort_order=$sort WHERE id=$id AND is_system=0;";
        BindGroup(cmd, group);
        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    public async Task DeleteGroupAsync(Guid id)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync().ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM asset_group WHERE id=$id AND is_system=0;";
        cmd.Parameters.AddWithValue("$id", id.ToString());
        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    // ─── Items ────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<AssetItem>> GetItemsAsync()
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync().ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT id, name, asset_type, group_id, currency, created_date, is_active, updated_at, loan_annual_rate, loan_term_months, loan_start_date, loan_handling_fee " +
            "FROM asset ORDER BY rowid;";
        return await ReadItemsAsync(cmd).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AssetItem>> GetItemsByTypeAsync(FinancialType type)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync().ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT id, name, asset_type, group_id, currency, created_date, is_active, updated_at, loan_annual_rate, loan_term_months, loan_start_date, loan_handling_fee " +
            "FROM asset WHERE asset_type=$type ORDER BY rowid;";
        cmd.Parameters.AddWithValue("$type", type.ToString());
        return await ReadItemsAsync(cmd).ConfigureAwait(false);
    }

    public async Task<AssetItem?> GetByIdAsync(Guid id)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync().ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT id, name, asset_type, group_id, currency, created_date, is_active, updated_at, loan_annual_rate, loan_term_months, loan_start_date, loan_handling_fee " +
            "FROM asset WHERE id=$id;";
        cmd.Parameters.AddWithValue("$id", id.ToString());
        await using var r = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
        return await r.ReadAsync().ConfigureAwait(false) ? MapItem(r) : null;
    }

    public async Task AddItemAsync(AssetItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync().ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR IGNORE INTO asset (id, name, asset_type, group_id, currency, created_date, is_active, updated_at,
                loan_annual_rate, loan_term_months, loan_start_date, loan_handling_fee)
            VALUES ($id, $name, $type, $grp, $cur, $dt, $ia, $ua, $lar, $ltm, $lsd, $lhf);
            """;
        BindItem(cmd, item);
        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    public async Task UpdateItemAsync(AssetItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync().ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "UPDATE asset SET name=$name, asset_type=$type, group_id=$grp, currency=$cur, " +
            "is_active=$ia, updated_at=$ua, " +
            "loan_annual_rate=$lar, loan_term_months=$ltm, loan_start_date=$lsd, loan_handling_fee=$lhf " +
            "WHERE id=$id;";
        BindItem(cmd, item);
        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    public async Task DeleteItemAsync(Guid id)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync().ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM asset WHERE id=$id;";
        cmd.Parameters.AddWithValue("$id", id.ToString());
        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    public async Task<Guid> FindOrCreateAccountAsync(string name, string currency, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);

        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);

        // 1. Try existing (is_active not considered — resurrect archived entries on upsert)
        Guid? existing = null;
        using (var sel = conn.CreateCommand())
        {
            sel.CommandText = "SELECT id FROM asset WHERE name = $n AND currency = $c AND asset_type = 'Asset' LIMIT 1";
            sel.Parameters.AddWithValue("$n", name);
            sel.Parameters.AddWithValue("$c", currency);
            var r = await sel.ExecuteScalarAsync(ct).ConfigureAwait(false);
            if (r is not null && r is not DBNull) existing = Guid.Parse((string)r);
        }
        if (existing.HasValue) return existing.Value;

        // 2. Insert new
        var id = Guid.NewGuid();
        try
        {
            using var ins = conn.CreateCommand();
            ins.CommandText = @"INSERT INTO asset(id, name, asset_type, group_id, currency, created_date, is_active, updated_at)
                                VALUES($id, $n, 'Asset', NULL, $c, $d, 1, datetime('now'))";
            ins.Parameters.AddWithValue("$id", id.ToString());
            ins.Parameters.AddWithValue("$n", name);
            ins.Parameters.AddWithValue("$c", currency);
            ins.Parameters.AddWithValue("$d", DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd"));
            await ins.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return id;
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19 /* SQLITE_CONSTRAINT */ )
        {
            // Race: another caller inserted between our SELECT and INSERT. Re-SELECT.
            using var sel2 = conn.CreateCommand();
            sel2.CommandText = "SELECT id FROM asset WHERE name = $n AND currency = $c AND asset_type = 'Asset' LIMIT 1";
            sel2.Parameters.AddWithValue("$n", name);
            sel2.Parameters.AddWithValue("$c", currency);
            var r2 = await sel2.ExecuteScalarAsync(ct).ConfigureAwait(false);
            if (r2 is string s) return Guid.Parse(s);
            throw; // genuinely unexpected
        }
    }

    public async Task ArchiveItemAsync(Guid id)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync().ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE asset SET is_active = 0, updated_at = datetime('now') WHERE id = $id";
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
        cmd.CommandText = @"SELECT COUNT(*) FROM trade
                            WHERE cash_account_id = $id OR to_cash_account_id = $id";
        cmd.Parameters.AddWithValue("$id", id.ToString());
        var r = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return r is null or DBNull ? 0 : Convert.ToInt32(r, System.Globalization.CultureInfo.InvariantCulture);
    }

    // ─── Events ───────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<AssetEvent>> GetEventsAsync(Guid assetId)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync().ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT id, asset_id, event_type, event_date, amount, quantity, " +
            "note, cash_account_id, created_at " +
            "FROM asset_event WHERE asset_id=$aid ORDER BY event_date DESC, rowid DESC;";
        cmd.Parameters.AddWithValue("$aid", assetId.ToString());
        var results = new List<AssetEvent>();
        await using var r = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
        while (await r.ReadAsync().ConfigureAwait(false))
            results.Add(MapEvent(r));
        return results;
    }

    public async Task AddEventAsync(AssetEvent evt)
    {
        ArgumentNullException.ThrowIfNull(evt);
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync().ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR IGNORE INTO asset_event
                (id, asset_id, event_type, event_date, amount, quantity, note, cash_account_id, created_at)
            VALUES ($id, $aid, $etype, $dt, $amt, $qty, $note, $cacct, $cat);
            """;
        cmd.Parameters.AddWithValue("$id",    evt.Id.ToString());
        cmd.Parameters.AddWithValue("$aid",   evt.AssetId.ToString());
        cmd.Parameters.AddWithValue("$etype", evt.EventType.ToString());
        cmd.Parameters.AddWithValue("$dt",    evt.EventDate.ToUniversalTime().ToString("o"));
        cmd.Parameters.AddWithValue("$amt",   evt.Amount.HasValue   ? (object)(double)evt.Amount.Value   : DBNull.Value);
        cmd.Parameters.AddWithValue("$qty",   evt.Quantity.HasValue ? (object)(double)evt.Quantity.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("$note",  evt.Note is not null  ? (object)evt.Note                  : DBNull.Value);
        cmd.Parameters.AddWithValue("$cacct", evt.CashAccountId.HasValue ? (object)evt.CashAccountId.Value.ToString() : DBNull.Value);
        cmd.Parameters.AddWithValue("$cat",   evt.CreatedAt.ToString("o"));
        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    public async Task DeleteEventAsync(Guid id)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync().ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM asset_event WHERE id=$id;";
        cmd.Parameters.AddWithValue("$id", id.ToString());
        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    public async Task<AssetEvent?> GetLatestValuationAsync(Guid assetId)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync().ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT id, asset_id, event_type, event_date, amount, quantity, " +
            "note, cash_account_id, created_at " +
            "FROM asset_event " +
            "WHERE asset_id=$aid AND event_type='Valuation' " +
            "ORDER BY event_date DESC, rowid DESC LIMIT 1;";
        cmd.Parameters.AddWithValue("$aid", assetId.ToString());
        await using var r = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
        return await r.ReadAsync().ConfigureAwait(false) ? MapEvent(r) : null;
    }

    // ─── Mappers & Binders ────────────────────────────────────────────────

    private static AssetGroup MapGroup(SqliteDataReader r) => new(
        Guid.Parse(r.GetString(0)),
        r.GetString(1),
        Enum.Parse<FinancialType>(r.GetString(2)),
        r.IsDBNull(3) ? null : r.GetString(3),
        r.GetInt32(4),
        r.GetInt32(5) != 0,
        DateOnly.Parse(r.GetString(6)));

    private static void BindGroup(SqliteCommand cmd, AssetGroup g)
    {
        cmd.Parameters.AddWithValue("$id",   g.Id.ToString());
        cmd.Parameters.AddWithValue("$name", g.Name);
        cmd.Parameters.AddWithValue("$type", g.Type.ToString());
        cmd.Parameters.AddWithValue("$icon", g.Icon is not null ? (object)g.Icon : DBNull.Value);
        cmd.Parameters.AddWithValue("$sort", g.SortOrder);
        cmd.Parameters.AddWithValue("$sys",  g.IsSystem ? 1 : 0);
        cmd.Parameters.AddWithValue("$dt",   g.CreatedDate.ToString("yyyy-MM-dd"));
    }

    private static AssetItem MapItem(SqliteDataReader r)
    {
        var isActive = r.IsDBNull(6) ? true : r.GetInt64(6) != 0;
        DateTime? updatedAt = null;
        if (!r.IsDBNull(7))
        {
            var s = r.GetString(7);
            if (!string.IsNullOrEmpty(s) && DateTime.TryParse(s, out var dt))
                updatedAt = dt;
        }
        decimal? loanAnnualRate  = r.IsDBNull(8)  ? null : (decimal)r.GetDouble(8);
        int?     loanTermMonths  = r.IsDBNull(9)  ? null : r.GetInt32(9);
        DateOnly? loanStartDate  = r.IsDBNull(10) ? null : DateOnly.Parse(r.GetString(10));
        decimal? loanHandlingFee = r.IsDBNull(11) ? null : (decimal)r.GetDouble(11);
        return new AssetItem(
            Guid.Parse(r.GetString(0)),
            r.GetString(1),
            Enum.Parse<FinancialType>(r.GetString(2)),
            r.IsDBNull(3) ? null : Guid.Parse(r.GetString(3)),
            r.IsDBNull(4) ? "TWD" : r.GetString(4),
            DateOnly.Parse(r.GetString(5)),
            isActive,
            updatedAt,
            loanAnnualRate,
            loanTermMonths,
            loanStartDate,
            loanHandlingFee);
    }

    private static void BindItem(SqliteCommand cmd, AssetItem i)
    {
        cmd.Parameters.AddWithValue("$id",   i.Id.ToString());
        cmd.Parameters.AddWithValue("$name", i.Name);
        cmd.Parameters.AddWithValue("$type", i.Type.ToString());
        cmd.Parameters.AddWithValue("$grp",  i.GroupId.HasValue ? (object)i.GroupId.Value.ToString() : DBNull.Value);
        cmd.Parameters.AddWithValue("$cur",  i.Currency);
        cmd.Parameters.AddWithValue("$dt",   i.CreatedDate.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$ia",  i.IsActive ? 1 : 0);
        cmd.Parameters.AddWithValue("$ua",  (i.UpdatedAt ?? DateTime.UtcNow).ToString("o"));
        cmd.Parameters.AddWithValue("$lar", i.LoanAnnualRate.HasValue  ? (object)(double)i.LoanAnnualRate.Value  : DBNull.Value);
        cmd.Parameters.AddWithValue("$ltm", i.LoanTermMonths.HasValue  ? (object)i.LoanTermMonths.Value          : DBNull.Value);
        cmd.Parameters.AddWithValue("$lsd", i.LoanStartDate.HasValue   ? (object)i.LoanStartDate.Value.ToString("yyyy-MM-dd") : DBNull.Value);
        cmd.Parameters.AddWithValue("$lhf", i.LoanHandlingFee.HasValue ? (object)(double)i.LoanHandlingFee.Value : DBNull.Value);
    }

    private static AssetEvent MapEvent(SqliteDataReader r) => new(
        Guid.Parse(r.GetString(0)),
        Guid.Parse(r.GetString(1)),
        Enum.Parse<AssetEventType>(r.GetString(2)),
        DateTime.Parse(r.GetString(3)),
        r.IsDBNull(4) ? null : (decimal)r.GetDouble(4),
        r.IsDBNull(5) ? null : (decimal)r.GetDouble(5),
        r.IsDBNull(6) ? null : r.GetString(6),
        r.IsDBNull(7) ? null : Guid.Parse(r.GetString(7)),
        DateTime.Parse(r.GetString(8)));

    private static async Task<IReadOnlyList<AssetItem>> ReadItemsAsync(SqliteCommand cmd)
    {
        var results = new List<AssetItem>();
        await using var r = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
        while (await r.ReadAsync().ConfigureAwait(false))
            results.Add(MapItem(r));
        return results;
    }
}
