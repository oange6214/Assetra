using Microsoft.Data.Sqlite;
using Assetra.Core.Interfaces;
using Assetra.Core.Interfaces.Sync;
using Assetra.Core.Models;
using Assetra.Core.Models.Sync;
using Assetra.Infrastructure.Sync;

namespace Assetra.Infrastructure.Persistence;

/// <summary>
/// SQLite implementation of <see cref="IAssetRepository"/>，同時實作 <see cref="IAssetSyncStore"/>（v0.20.8）。
///
/// <para>
/// 鏡 <see cref="TradeSqliteRepository"/> 的 sync 模式：每筆 mutation stamp version、
/// last_modified_at、is_pending_push=1；DeleteItem 改為 soft delete（保留 row、is_deleted=1、bump version）。
/// AssetGroup 與 AssetEvent 仍為 hard delete，未接 sync（後續 sprint 規劃）。
/// </para>
/// </summary>
public sealed class AssetSqliteRepository : IAssetRepository, IAssetSyncStore, IAssetGroupSyncStore, IAssetEventSyncStore
{
    private readonly string _connectionString;
    private readonly string _deviceId;
    private readonly TimeProvider _time;

    public AssetSqliteRepository(string dbPath, string deviceId = "local", TimeProvider? time = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(deviceId);
        _connectionString = $"Data Source={dbPath}";
        _deviceId = deviceId;
        _time = time ?? TimeProvider.System;
        AssetSchemaMigrator.EnsureInitialized(_connectionString);
    }

    private const string ItemSelectClause =
        "id, name, asset_type, group_id, currency, created_date, is_active, updated_at, " +
        "loan_annual_rate, loan_term_months, loan_start_date, loan_handling_fee, " +
        "liability_subtype, billing_day, due_day, credit_limit, issuer_name, subtype";

    private const string ItemSyncSelectClause = ItemSelectClause +
        ", version, last_modified_at, last_modified_by_device, is_deleted";

    // ─── Groups ───────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<AssetGroup>> GetGroupsAsync()
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync().ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT id, name, asset_type, icon, sort_order, is_system, created_date " +
            "FROM asset_group WHERE is_deleted = 0 ORDER BY asset_type, sort_order, rowid;";
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
            INSERT OR IGNORE INTO asset_group
                (id, name, asset_type, icon, sort_order, is_system, created_date,
                 version, last_modified_at, last_modified_by_device, is_deleted, is_pending_push)
            VALUES
                ($id, $name, $type, $icon, $sort, $sys, $dt,
                 1, $now, $device, 0, 1);
            """;
        BindGroup(cmd, group);
        StampSyncParams(cmd);
        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    public async Task UpdateGroupAsync(AssetGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync().ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE asset_group SET
                name=$name, asset_type=$type, icon=$icon, sort_order=$sort,
                version = version + 1,
                last_modified_at = $now,
                last_modified_by_device = $device,
                is_pending_push = 1
            WHERE id=$id AND is_system=0;
            """;
        BindGroup(cmd, group);
        StampSyncParams(cmd);
        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    public async Task DeleteGroupAsync(Guid id)
    {
        // Soft delete; system groups unaffected (WHERE is_system=0).
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync().ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE asset_group SET
                is_deleted = 1,
                version = version + 1,
                last_modified_at = $now,
                last_modified_by_device = $device,
                is_pending_push = 1
            WHERE id=$id AND is_system=0;
            """;
        cmd.Parameters.AddWithValue("$id", id.ToString());
        StampSyncParams(cmd);
        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    // ─── Items (filter is_deleted = 0) ──────────────────────────────────

    public async Task<IReadOnlyList<AssetItem>> GetItemsAsync()
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync().ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {ItemSelectClause} FROM asset WHERE is_deleted = 0 ORDER BY rowid;";
        return await ReadItemsAsync(cmd).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AssetItem>> GetItemsByTypeAsync(FinancialType type)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync().ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {ItemSelectClause} FROM asset WHERE is_deleted = 0 AND asset_type=$type ORDER BY rowid;";
        cmd.Parameters.AddWithValue("$type", type.ToString());
        return await ReadItemsAsync(cmd).ConfigureAwait(false);
    }

    public async Task<AssetItem?> GetByIdAsync(Guid id)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync().ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {ItemSelectClause} FROM asset WHERE id=$id AND is_deleted = 0;";
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
        cmd.CommandText = InsertItemSql;
        BindItem(cmd, item);
        StampSyncParams(cmd);
        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    public async Task UpdateItemAsync(AssetItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync().ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE asset SET
                name=$name, asset_type=$type, group_id=$grp, currency=$cur,
                is_active=$ia, updated_at=$ua,
                loan_annual_rate=$lar, loan_term_months=$ltm, loan_start_date=$lsd, loan_handling_fee=$lhf,
                liability_subtype=$lst, billing_day=$bill, due_day=$due, credit_limit=$limit,
                issuer_name=$issuer, subtype=$sub,
                version = version + 1,
                last_modified_at = $now,
                last_modified_by_device = $device,
                is_pending_push = 1
            WHERE id=$id;
            """;
        BindItem(cmd, item);
        StampSyncParams(cmd);
        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    public async Task DeleteItemAsync(Guid id)
    {
        // Soft delete (tombstone): keep row so deletion can be pushed to remote.
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync().ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE asset SET
                is_deleted = 1,
                is_active = 0,
                version = version + 1,
                updated_at = $now,
                last_modified_at = $now,
                last_modified_by_device = $device,
                is_pending_push = 1
            WHERE id=$id;
            """;
        cmd.Parameters.AddWithValue("$id", id.ToString());
        StampSyncParams(cmd);
        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    public async Task<Guid> FindOrCreateAccountAsync(string name, string currency, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);

        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);

        // 1. Try existing live row (resurrect tombstones too).
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

        // 2. Insert new (stamp sync metadata).
        var id = Guid.NewGuid();
        var now = NowIso();
        try
        {
            using var ins = conn.CreateCommand();
            ins.CommandText = """
                INSERT INTO asset(id, name, asset_type, group_id, currency, created_date, is_active, updated_at,
                                  version, last_modified_at, last_modified_by_device, is_deleted, is_pending_push)
                VALUES($id, $n, 'Asset', NULL, $c, $d, 1, $now,
                       1, $now, $device, 0, 1);
                """;
            ins.Parameters.AddWithValue("$id", id.ToString());
            ins.Parameters.AddWithValue("$n", name);
            ins.Parameters.AddWithValue("$c", currency);
            ins.Parameters.AddWithValue("$d", DateOnly.FromDateTime(_time.GetUtcNow().UtcDateTime).ToString("yyyy-MM-dd"));
            ins.Parameters.AddWithValue("$now", now);
            ins.Parameters.AddWithValue("$device", _deviceId);
            await ins.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return id;
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19 /* SQLITE_CONSTRAINT */ )
        {
            using var sel2 = conn.CreateCommand();
            sel2.CommandText = "SELECT id FROM asset WHERE name = $n AND currency = $c AND asset_type = 'Asset' LIMIT 1";
            sel2.Parameters.AddWithValue("$n", name);
            sel2.Parameters.AddWithValue("$c", currency);
            var r2 = await sel2.ExecuteScalarAsync(ct).ConfigureAwait(false);
            if (r2 is string s) return Guid.Parse(s);
            throw;
        }
    }

    public async Task ArchiveItemAsync(Guid id)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync().ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE asset SET
                is_active = 0,
                updated_at = $now,
                version = version + 1,
                last_modified_at = $now,
                last_modified_by_device = $device,
                is_pending_push = 1
            WHERE id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", id.ToString());
        StampSyncParams(cmd);
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
                            WHERE is_deleted = 0 AND (cash_account_id = $id OR to_cash_account_id = $id)";
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
            "FROM asset_event WHERE asset_id=$aid AND is_deleted = 0 ORDER BY event_date DESC, rowid DESC;";
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
                (id, asset_id, event_type, event_date, amount, quantity, note, cash_account_id, created_at,
                 version, last_modified_at, last_modified_by_device, is_deleted, is_pending_push)
            VALUES
                ($id, $aid, $etype, $dt, $amt, $qty, $note, $cacct, $cat,
                 1, $now, $device, 0, 1);
            """;
        BindEvent(cmd, evt);
        StampSyncParams(cmd);
        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    public async Task DeleteEventAsync(Guid id)
    {
        // Soft delete to support cross-device sync.
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync().ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE asset_event SET
                is_deleted = 1,
                version = version + 1,
                last_modified_at = $now,
                last_modified_by_device = $device,
                is_pending_push = 1
            WHERE id=$id;
            """;
        cmd.Parameters.AddWithValue("$id", id.ToString());
        StampSyncParams(cmd);
        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<Guid, AssetEvent>> GetLatestValuationsAsync(
        IEnumerable<Guid> assetIds, CancellationToken ct = default)
    {
        var idList = assetIds.Distinct().ToList();
        var result = new Dictionary<Guid, AssetEvent>();
        if (idList.Count == 0) return result;

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();

        var inClause = string.Join(",", idList.Select((_, i) => $"$id{i}"));
        cmd.CommandText = $"""
            WITH ranked AS (
                SELECT id, asset_id, event_type, event_date, amount, quantity,
                       note, cash_account_id, created_at,
                       ROW_NUMBER() OVER (PARTITION BY asset_id ORDER BY event_date DESC, rowid DESC) AS rn
                FROM asset_event
                WHERE is_deleted = 0 AND event_type='Valuation' AND asset_id IN ({inClause})
            )
            SELECT id, asset_id, event_type, event_date, amount, quantity, note, cash_account_id, created_at
            FROM ranked WHERE rn = 1;
            """;
        for (int i = 0; i < idList.Count; i++)
            cmd.Parameters.AddWithValue($"$id{i}", idList[i].ToString());

        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            var evt = MapEvent(r);
            result[evt.AssetId] = evt;
        }
        return result;
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
            "WHERE asset_id=$aid AND is_deleted = 0 AND event_type='Valuation' " +
            "ORDER BY event_date DESC, rowid DESC LIMIT 1;";
        cmd.Parameters.AddWithValue("$aid", assetId.ToString());
        await using var r = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
        return await r.ReadAsync().ConfigureAwait(false) ? MapEvent(r) : null;
    }

    // ─── IAssetSyncStore ────────────────────────────────────────────────────

    public async Task<IReadOnlyList<SyncEnvelope>> GetPendingPushAsync(CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {ItemSyncSelectClause} FROM asset WHERE is_pending_push = 1;";
        var results = new List<SyncEnvelope>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var item = MapItem(reader);
            var version = new EntityVersion(
                Version: reader.GetInt64(18),
                LastModifiedAt: DateTimeOffset.Parse(reader.GetString(19)),
                LastModifiedByDevice: reader.GetString(20));
            var isDeleted = reader.GetInt32(21) != 0;
            results.Add(AssetSyncMapper.ToEnvelope(item, version, isDeleted));
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
        cmd.CommandText = "UPDATE asset SET is_pending_push = 0 WHERE id = $id;";
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
            if (env.EntityType != AssetSyncMapper.EntityType) continue;

            await using (var probe = conn.CreateCommand())
            {
                probe.Transaction = tx;
                probe.CommandText = "SELECT version FROM asset WHERE id = $id;";
                probe.Parameters.AddWithValue("$id", env.EntityId.ToString());
                var existing = await probe.ExecuteScalarAsync(ct).ConfigureAwait(false);
                if (existing is not null && Convert.ToInt64(existing) >= env.Version.Version)
                    continue; // never write backwards
            }

            if (env.Deleted)
            {
                // Tombstone: minimum NOT NULL placeholders for new rows.
                await using var del = conn.CreateCommand();
                del.Transaction = tx;
                del.CommandText = """
                    INSERT INTO asset
                        (id, name, asset_type, currency, created_date, is_active, updated_at,
                         version, last_modified_at, last_modified_by_device, is_deleted, is_pending_push)
                    VALUES
                        ($id, '', 'Asset', 'TWD', $date, 0, $now,
                         $ver, $modAt, $modBy, 1, 0)
                    ON CONFLICT(id) DO UPDATE SET
                        is_deleted = 1,
                        is_active = 0,
                        version = excluded.version,
                        last_modified_at = excluded.last_modified_at,
                        last_modified_by_device = excluded.last_modified_by_device,
                        updated_at = excluded.updated_at,
                        is_pending_push = 0;
                    """;
                del.Parameters.AddWithValue("$id", env.EntityId.ToString());
                del.Parameters.AddWithValue("$date", DateOnly.FromDateTime(_time.GetUtcNow().UtcDateTime).ToString("yyyy-MM-dd"));
                del.Parameters.AddWithValue("$now", NowIso());
                del.Parameters.AddWithValue("$ver", env.Version.Version);
                del.Parameters.AddWithValue("$modAt", env.Version.LastModifiedAt.UtcDateTime.ToString("o"));
                del.Parameters.AddWithValue("$modBy", env.Version.LastModifiedByDevice);
                await del.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                continue;
            }

            var item = AssetSyncMapper.FromPayload(env);
            await using var up = conn.CreateCommand();
            up.Transaction = tx;
            up.CommandText = """
                INSERT INTO asset
                    (id, name, asset_type, group_id, currency, created_date, is_active, updated_at,
                     loan_annual_rate, loan_term_months, loan_start_date, loan_handling_fee,
                     liability_subtype, billing_day, due_day, credit_limit, issuer_name, subtype,
                     version, last_modified_at, last_modified_by_device, is_deleted, is_pending_push)
                VALUES
                    ($id, $name, $type, $grp, $cur, $dt, $ia, $ua,
                     $lar, $ltm, $lsd, $lhf,
                     $lst, $bill, $due, $limit, $issuer, $sub,
                     $ver, $modAt, $modBy, 0, 0)
                ON CONFLICT(id) DO UPDATE SET
                    name = excluded.name,
                    asset_type = excluded.asset_type,
                    group_id = excluded.group_id,
                    currency = excluded.currency,
                    created_date = excluded.created_date,
                    is_active = excluded.is_active,
                    updated_at = excluded.updated_at,
                    loan_annual_rate = excluded.loan_annual_rate,
                    loan_term_months = excluded.loan_term_months,
                    loan_start_date = excluded.loan_start_date,
                    loan_handling_fee = excluded.loan_handling_fee,
                    liability_subtype = excluded.liability_subtype,
                    billing_day = excluded.billing_day,
                    due_day = excluded.due_day,
                    credit_limit = excluded.credit_limit,
                    issuer_name = excluded.issuer_name,
                    subtype = excluded.subtype,
                    version = excluded.version,
                    last_modified_at = excluded.last_modified_at,
                    last_modified_by_device = excluded.last_modified_by_device,
                    is_deleted = 0,
                    is_pending_push = 0;
                """;
            BindItem(up, item);
            up.Parameters.AddWithValue("$ver", env.Version.Version);
            up.Parameters.AddWithValue("$modAt", env.Version.LastModifiedAt.UtcDateTime.ToString("o"));
            up.Parameters.AddWithValue("$modBy", env.Version.LastModifiedByDevice);
            await up.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await tx.CommitAsync(ct).ConfigureAwait(false);
    }

    // ─── Helpers ──────────────────────────────────────────────────────────

    private const string InsertItemSql = """
        INSERT INTO asset
            (id, name, asset_type, group_id, currency, created_date, is_active, updated_at,
             loan_annual_rate, loan_term_months, loan_start_date, loan_handling_fee,
             liability_subtype, billing_day, due_day, credit_limit, issuer_name, subtype,
             version, last_modified_at, last_modified_by_device, is_deleted, is_pending_push)
        VALUES
            ($id, $name, $type, $grp, $cur, $dt, $ia, $ua,
             $lar, $ltm, $lsd, $lhf,
             $lst, $bill, $due, $limit, $issuer, $sub,
             1, $now, $device, 0, 1);
        """;

    private string NowIso() => _time.GetUtcNow().UtcDateTime.ToString("o");

    private void StampSyncParams(SqliteCommand cmd)
    {
        cmd.Parameters.AddWithValue("$now", NowIso());
        cmd.Parameters.AddWithValue("$device", _deviceId);
    }

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

    private static void BindEvent(SqliteCommand cmd, AssetEvent evt)
    {
        cmd.Parameters.AddWithValue("$id",    evt.Id.ToString());
        cmd.Parameters.AddWithValue("$aid",   evt.AssetId.ToString());
        cmd.Parameters.AddWithValue("$etype", evt.EventType.ToString());
        cmd.Parameters.AddWithValue("$dt",    evt.EventDate.ToUniversalTime().ToString("o"));
        cmd.Parameters.AddWithValue("$amt",   evt.Amount.HasValue   ? (object)(double)evt.Amount.Value   : DBNull.Value);
        cmd.Parameters.AddWithValue("$qty",   evt.Quantity.HasValue ? (object)(double)evt.Quantity.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("$note",  evt.Note is not null  ? (object)evt.Note                  : DBNull.Value);
        cmd.Parameters.AddWithValue("$cacct", evt.CashAccountId.HasValue ? (object)evt.CashAccountId.Value.ToString() : DBNull.Value);
        cmd.Parameters.AddWithValue("$cat",   evt.CreatedAt.ToString("o"));
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

    // ─── IAssetGroupSyncStore ───────────────────────────────────────────────

    public async Task<IReadOnlyList<SyncEnvelope>> GetGroupPendingPushAsync(CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT id, name, asset_type, icon, sort_order, is_system, created_date, " +
            "version, last_modified_at, last_modified_by_device, is_deleted " +
            "FROM asset_group WHERE is_pending_push = 1;";
        var results = new List<SyncEnvelope>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            var group = MapGroup(r);
            var version = new EntityVersion(
                Version: r.GetInt64(7),
                LastModifiedAt: DateTimeOffset.Parse(r.GetString(8)),
                LastModifiedByDevice: r.GetString(9));
            var isDeleted = r.GetInt32(10) != 0;
            results.Add(AssetGroupSyncMapper.ToEnvelope(group, version, isDeleted));
        }
        return results;
    }

    public async Task MarkGroupPushedAsync(IReadOnlyList<Guid> ids, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.Count == 0) return;
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "UPDATE asset_group SET is_pending_push = 0 WHERE id = $id;";
        var p = cmd.Parameters.Add("$id", SqliteType.Text);
        foreach (var id in ids)
        {
            p.Value = id.ToString();
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        await tx.CommitAsync(ct).ConfigureAwait(false);
    }

    public async Task ApplyGroupRemoteAsync(IReadOnlyList<SyncEnvelope> envelopes, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(envelopes);
        if (envelopes.Count == 0) return;
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct).ConfigureAwait(false);

        foreach (var env in envelopes)
        {
            if (env.EntityType != AssetGroupSyncMapper.EntityType) continue;

            // Protect system groups: never let remote tombstone delete a seeded system group.
            await using (var probe = conn.CreateCommand())
            {
                probe.Transaction = tx;
                probe.CommandText = "SELECT version, is_system FROM asset_group WHERE id = $id;";
                probe.Parameters.AddWithValue("$id", env.EntityId.ToString());
                await using var rr = await probe.ExecuteReaderAsync(ct).ConfigureAwait(false);
                if (await rr.ReadAsync(ct).ConfigureAwait(false))
                {
                    var existingVersion = rr.GetInt64(0);
                    var isSystem = rr.GetInt32(1) != 0;
                    if (existingVersion >= env.Version.Version) continue;
                    if (isSystem && env.Deleted) continue;
                }
            }

            if (env.Deleted)
            {
                // Tombstone: NOT NULL placeholders for new rows.
                await using var del = conn.CreateCommand();
                del.Transaction = tx;
                del.CommandText = """
                    INSERT INTO asset_group
                        (id, name, asset_type, icon, sort_order, is_system, created_date,
                         version, last_modified_at, last_modified_by_device, is_deleted, is_pending_push)
                    VALUES
                        ($id, '', 'Asset', NULL, 0, 0, $date,
                         $ver, $modAt, $modBy, 1, 0)
                    ON CONFLICT(id) DO UPDATE SET
                        is_deleted = 1,
                        version = excluded.version,
                        last_modified_at = excluded.last_modified_at,
                        last_modified_by_device = excluded.last_modified_by_device,
                        is_pending_push = 0;
                    """;
                del.Parameters.AddWithValue("$id", env.EntityId.ToString());
                del.Parameters.AddWithValue("$date", DateOnly.FromDateTime(_time.GetUtcNow().UtcDateTime).ToString("yyyy-MM-dd"));
                del.Parameters.AddWithValue("$ver", env.Version.Version);
                del.Parameters.AddWithValue("$modAt", env.Version.LastModifiedAt.UtcDateTime.ToString("o"));
                del.Parameters.AddWithValue("$modBy", env.Version.LastModifiedByDevice);
                await del.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                continue;
            }

            var group = AssetGroupSyncMapper.FromPayload(env);
            await using var up = conn.CreateCommand();
            up.Transaction = tx;
            up.CommandText = """
                INSERT INTO asset_group
                    (id, name, asset_type, icon, sort_order, is_system, created_date,
                     version, last_modified_at, last_modified_by_device, is_deleted, is_pending_push)
                VALUES
                    ($id, $name, $type, $icon, $sort, $sys, $dt,
                     $ver, $modAt, $modBy, 0, 0)
                ON CONFLICT(id) DO UPDATE SET
                    name = excluded.name,
                    asset_type = excluded.asset_type,
                    icon = excluded.icon,
                    sort_order = excluded.sort_order,
                    version = excluded.version,
                    last_modified_at = excluded.last_modified_at,
                    last_modified_by_device = excluded.last_modified_by_device,
                    is_deleted = 0,
                    is_pending_push = 0;
                """;
            BindGroup(up, group);
            up.Parameters.AddWithValue("$ver", env.Version.Version);
            up.Parameters.AddWithValue("$modAt", env.Version.LastModifiedAt.UtcDateTime.ToString("o"));
            up.Parameters.AddWithValue("$modBy", env.Version.LastModifiedByDevice);
            await up.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await tx.CommitAsync(ct).ConfigureAwait(false);
    }

    // ─── IAssetEventSyncStore ───────────────────────────────────────────────

    public async Task<IReadOnlyList<SyncEnvelope>> GetEventPendingPushAsync(CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT id, asset_id, event_type, event_date, amount, quantity, note, cash_account_id, created_at, " +
            "version, last_modified_at, last_modified_by_device, is_deleted " +
            "FROM asset_event WHERE is_pending_push = 1;";
        var results = new List<SyncEnvelope>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            var evt = MapEvent(r);
            var version = new EntityVersion(
                Version: r.GetInt64(9),
                LastModifiedAt: DateTimeOffset.Parse(r.GetString(10)),
                LastModifiedByDevice: r.GetString(11));
            var isDeleted = r.GetInt32(12) != 0;
            results.Add(AssetEventSyncMapper.ToEnvelope(evt, version, isDeleted));
        }
        return results;
    }

    public async Task MarkEventPushedAsync(IReadOnlyList<Guid> ids, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.Count == 0) return;
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "UPDATE asset_event SET is_pending_push = 0 WHERE id = $id;";
        var p = cmd.Parameters.Add("$id", SqliteType.Text);
        foreach (var id in ids)
        {
            p.Value = id.ToString();
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        await tx.CommitAsync(ct).ConfigureAwait(false);
    }

    public async Task ApplyEventRemoteAsync(IReadOnlyList<SyncEnvelope> envelopes, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(envelopes);
        if (envelopes.Count == 0) return;
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct).ConfigureAwait(false);

        foreach (var env in envelopes)
        {
            if (env.EntityType != AssetEventSyncMapper.EntityType) continue;

            bool existsLocally = false;
            await using (var probe = conn.CreateCommand())
            {
                probe.Transaction = tx;
                probe.CommandText = "SELECT version FROM asset_event WHERE id = $id;";
                probe.Parameters.AddWithValue("$id", env.EntityId.ToString());
                var existing = await probe.ExecuteScalarAsync(ct).ConfigureAwait(false);
                if (existing is not null)
                {
                    existsLocally = true;
                    if (Convert.ToInt64(existing) >= env.Version.Version) continue;
                }
            }

            if (env.Deleted)
            {
                // Skip tombstones for unknown ids: asset_event has FK to asset(id), so we cannot
                // insert a phantom tombstone row. A delete of "nothing" is a no-op.
                if (!existsLocally) continue;

                // Tombstone of an existing row: ON CONFLICT path keeps original asset_id.
                await using var del = conn.CreateCommand();
                del.Transaction = tx;
                del.CommandText = """
                    INSERT INTO asset_event
                        (id, asset_id, event_type, event_date, created_at,
                         version, last_modified_at, last_modified_by_device, is_deleted, is_pending_push)
                    VALUES
                        ($id, $aid, 'Valuation', $now, $now,
                         $ver, $modAt, $modBy, 1, 0)
                    ON CONFLICT(id) DO UPDATE SET
                        is_deleted = 1,
                        version = excluded.version,
                        last_modified_at = excluded.last_modified_at,
                        last_modified_by_device = excluded.last_modified_by_device,
                        is_pending_push = 0;
                    """;
                del.Parameters.AddWithValue("$id", env.EntityId.ToString());
                del.Parameters.AddWithValue("$aid", Guid.Empty.ToString());
                del.Parameters.AddWithValue("$now", NowIso());
                del.Parameters.AddWithValue("$ver", env.Version.Version);
                del.Parameters.AddWithValue("$modAt", env.Version.LastModifiedAt.UtcDateTime.ToString("o"));
                del.Parameters.AddWithValue("$modBy", env.Version.LastModifiedByDevice);
                await del.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                continue;
            }

            var evt = AssetEventSyncMapper.FromPayload(env);
            await using var up = conn.CreateCommand();
            up.Transaction = tx;
            up.CommandText = """
                INSERT INTO asset_event
                    (id, asset_id, event_type, event_date, amount, quantity, note, cash_account_id, created_at,
                     version, last_modified_at, last_modified_by_device, is_deleted, is_pending_push)
                VALUES
                    ($id, $aid, $etype, $dt, $amt, $qty, $note, $cacct, $cat,
                     $ver, $modAt, $modBy, 0, 0)
                ON CONFLICT(id) DO UPDATE SET
                    asset_id = excluded.asset_id,
                    event_type = excluded.event_type,
                    event_date = excluded.event_date,
                    amount = excluded.amount,
                    quantity = excluded.quantity,
                    note = excluded.note,
                    cash_account_id = excluded.cash_account_id,
                    created_at = excluded.created_at,
                    version = excluded.version,
                    last_modified_at = excluded.last_modified_at,
                    last_modified_by_device = excluded.last_modified_by_device,
                    is_deleted = 0,
                    is_pending_push = 0;
                """;
            BindEvent(up, evt);
            up.Parameters.AddWithValue("$ver", env.Version.Version);
            up.Parameters.AddWithValue("$modAt", env.Version.LastModifiedAt.UtcDateTime.ToString("o"));
            up.Parameters.AddWithValue("$modBy", env.Version.LastModifiedByDevice);
            await up.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await tx.CommitAsync(ct).ConfigureAwait(false);
    }
}
