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

    public AssetSqliteRepository(string dbPath)
    {
        _connectionString = $"Data Source={dbPath}";
        AssetSchemaMigrator.EnsureInitialized(_connectionString);
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
            "SELECT id, name, asset_type, group_id, currency, created_date, is_active, updated_at, loan_annual_rate, loan_term_months, loan_start_date, loan_handling_fee, liability_subtype, billing_day, due_day, credit_limit, issuer_name, subtype " +
            "FROM asset ORDER BY rowid;";
        return await ReadItemsAsync(cmd).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AssetItem>> GetItemsByTypeAsync(FinancialType type)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync().ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT id, name, asset_type, group_id, currency, created_date, is_active, updated_at, loan_annual_rate, loan_term_months, loan_start_date, loan_handling_fee, liability_subtype, billing_day, due_day, credit_limit, issuer_name, subtype " +
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
            "SELECT id, name, asset_type, group_id, currency, created_date, is_active, updated_at, loan_annual_rate, loan_term_months, loan_start_date, loan_handling_fee, liability_subtype, billing_day, due_day, credit_limit, issuer_name, subtype " +
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
            INSERT INTO asset (id, name, asset_type, group_id, currency, created_date, is_active, updated_at,
                loan_annual_rate, loan_term_months, loan_start_date, loan_handling_fee,
                liability_subtype, billing_day, due_day, credit_limit, issuer_name, subtype)
            VALUES ($id, $name, $type, $grp, $cur, $dt, $ia, $ua, $lar, $ltm, $lsd, $lhf,
                $lst, $bill, $due, $limit, $issuer, $sub);
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
            "loan_annual_rate=$lar, loan_term_months=$ltm, loan_start_date=$lsd, loan_handling_fee=$lhf, " +
            "liability_subtype=$lst, billing_day=$bill, due_day=$due, credit_limit=$limit, issuer_name=$issuer, subtype=$sub " +
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
        LiabilitySubtype? liabilitySubtype = r.IsDBNull(12) ? null : Enum.Parse<LiabilitySubtype>(r.GetString(12));
        int? billingDay = r.IsDBNull(13) ? null : r.GetInt32(13);
        int? dueDay = r.IsDBNull(14) ? null : r.GetInt32(14);
        decimal? creditLimit = r.IsDBNull(15) ? null : (decimal)r.GetDouble(15);
        string? issuerName = r.IsDBNull(16) ? null : r.GetString(16);
        string? subtype = r.IsDBNull(17) ? null : r.GetString(17);
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
            loanHandlingFee,
            liabilitySubtype,
            billingDay,
            dueDay,
            creditLimit,
            issuerName,
            subtype);
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
        cmd.Parameters.AddWithValue("$lst", i.LiabilitySubtype.HasValue ? (object)i.LiabilitySubtype.Value.ToString() : DBNull.Value);
        cmd.Parameters.AddWithValue("$bill", i.BillingDay.HasValue ? (object)i.BillingDay.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("$due", i.DueDay.HasValue ? (object)i.DueDay.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("$limit", i.CreditLimit.HasValue ? (object)(double)i.CreditLimit.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("$issuer", i.IssuerName is not null ? (object)i.IssuerName : DBNull.Value);
        cmd.Parameters.AddWithValue("$sub", i.Subtype is not null ? (object)i.Subtype : DBNull.Value);
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
