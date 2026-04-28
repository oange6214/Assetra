using Assetra.Core.Interfaces;
using Assetra.Core.Interfaces.Sync;
using Assetra.Core.Models;
using Assetra.Core.Models.Sync;
using Assetra.Infrastructure.Sync;
using Microsoft.Data.Sqlite;

namespace Assetra.Infrastructure.Persistence;

/// <summary>
/// SQLite implementation of <see cref="IPortfolioRepository"/>，同時實作 <see cref="IPortfolioSyncStore"/>（v0.20.9）。
///
/// <para>
/// 鏡 <see cref="AssetSqliteRepository"/> 的 sync 模式：每筆 mutation stamp version、
/// last_modified_at、is_pending_push=1；RemoveAsync 改為 soft delete（保留 row、is_deleted=1、bump version）。
/// </para>
/// </summary>
public sealed class PortfolioSqliteRepository : IPortfolioRepository, IPortfolioSyncStore
{
    private readonly string _connectionString;
    private readonly Func<string> _deviceIdProvider;
    private readonly TimeProvider _time;

    public PortfolioSqliteRepository(string dbPath, string deviceId = "local", TimeProvider? time = null)
        : this(dbPath, () => deviceId, time)
    {
    }

    public PortfolioSqliteRepository(string dbPath, Func<string> deviceIdProvider, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(deviceIdProvider);
        _connectionString = $"Data Source={dbPath}";
        _deviceIdProvider = deviceIdProvider;
        _time = time ?? TimeProvider.System;
        PortfolioSchemaMigrator.EnsureInitialized(_connectionString);
    }

    private const string SelectClause =
        "id, symbol, exchange, asset_type, display_name, currency, is_active, is_etf";

    private const string SyncSelectClause = SelectClause +
        ", version, last_modified_at, last_modified_by_device, is_deleted";

    public async Task<IReadOnlyList<PortfolioEntry>> GetEntriesAsync(CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {SelectClause} FROM portfolio WHERE is_deleted = 0 ORDER BY rowid;";
        return await ReadEntriesAsync(cmd, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PortfolioEntry>> GetActiveAsync(CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {SelectClause} FROM portfolio WHERE is_deleted = 0 AND is_active = 1 ORDER BY rowid;";
        return await ReadEntriesAsync(cmd, ct).ConfigureAwait(false);
    }

    public async Task AddAsync(PortfolioEntry entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR IGNORE INTO portfolio
                (id, symbol, exchange, asset_type, display_name, currency,
                 created_at, updated_at, is_active, is_etf,
                 version, last_modified_at, last_modified_by_device, is_deleted, is_pending_push)
            VALUES
                ($id, $sym, $ex, $at, $dn, $cur,
                 $now, $now, $ia, $etf,
                 1, $now, $device, 0, 1);
            """;
        BindEntry(cmd, entry);
        StampSyncParams(cmd);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task UpdateAsync(PortfolioEntry entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE portfolio SET
                asset_type=$at,
                updated_at=$now,
                version = version + 1,
                last_modified_at = $now,
                last_modified_by_device = $device,
                is_pending_push = 1
            WHERE id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", entry.Id.ToString());
        cmd.Parameters.AddWithValue("$at", entry.AssetType.ToString());
        StampSyncParams(cmd);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task UpdateMetadataAsync(Guid id, string displayName, string currency, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE portfolio SET
                display_name=$dn,
                currency=$cur,
                updated_at=$now,
                version = version + 1,
                last_modified_at = $now,
                last_modified_by_device = $device,
                is_pending_push = 1
            WHERE id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", id.ToString());
        cmd.Parameters.AddWithValue("$dn", displayName);
        cmd.Parameters.AddWithValue("$cur", currency);
        StampSyncParams(cmd);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task RemoveAsync(Guid id, CancellationToken ct = default)
    {
        // Soft delete (tombstone): keep row so deletion can be pushed to remote.
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE portfolio SET
                is_deleted = 1,
                is_active = 0,
                version = version + 1,
                updated_at = $now,
                last_modified_at = $now,
                last_modified_by_device = $device,
                is_pending_push = 1
            WHERE id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", id.ToString());
        StampSyncParams(cmd);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task ArchiveAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE portfolio SET
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
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task UnarchiveAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE portfolio SET
                is_active = 1,
                updated_at = $now,
                version = version + 1,
                last_modified_at = $now,
                last_modified_by_device = $device,
                is_pending_push = 1
            WHERE id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", id.ToString());
        StampSyncParams(cmd);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<Guid> FindOrCreatePortfolioEntryAsync(
        string symbol, string exchange, string? displayName, AssetType assetType,
        string? currency = null,
        bool isEtf = false,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        ArgumentException.ThrowIfNullOrWhiteSpace(exchange);
        var resolvedCurrency = string.IsNullOrWhiteSpace(currency) ? "TWD" : currency.Trim().ToUpperInvariant();

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);

        await using (var sel = conn.CreateCommand())
        {
            sel.CommandText = "SELECT id FROM portfolio WHERE symbol = $s AND exchange = $e AND is_deleted = 0 LIMIT 1";
            sel.Parameters.AddWithValue("$s", symbol);
            sel.Parameters.AddWithValue("$e", exchange);
            var r = await sel.ExecuteScalarAsync(ct).ConfigureAwait(false);
            if (r is string s1)
                return Guid.Parse(s1);
        }

        var id = Guid.NewGuid();
        try
        {
            await using var ins = conn.CreateCommand();
            ins.CommandText = """
                INSERT INTO portfolio
                    (id, symbol, exchange, asset_type, created_at, updated_at,
                     display_name, currency, is_active, is_etf,
                     version, last_modified_at, last_modified_by_device, is_deleted, is_pending_push)
                VALUES
                    ($id, $s, $e, $t, $now, $now,
                     $dn, $cur, 1, $etf,
                     1, $now, $device, 0, 1);
                """;
            ins.Parameters.AddWithValue("$id", id.ToString());
            ins.Parameters.AddWithValue("$s", symbol);
            ins.Parameters.AddWithValue("$e", exchange);
            ins.Parameters.AddWithValue("$t", assetType.ToString());
            ins.Parameters.AddWithValue("$dn", (object?)displayName ?? "");
            ins.Parameters.AddWithValue("$cur", resolvedCurrency);
            ins.Parameters.AddWithValue("$etf", isEtf ? 1 : 0);
            StampSyncParams(ins);
            await ins.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return id;
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19 /* SQLITE_CONSTRAINT */)
        {
            await using var sel2 = conn.CreateCommand();
            sel2.CommandText = "SELECT id FROM portfolio WHERE symbol = $s AND exchange = $e AND is_deleted = 0 LIMIT 1";
            sel2.Parameters.AddWithValue("$s", symbol);
            sel2.Parameters.AddWithValue("$e", exchange);
            var r2 = await sel2.ExecuteScalarAsync(ct).ConfigureAwait(false);
            if (r2 is string s2)
                return Guid.Parse(s2);
            throw;
        }
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
        cmd.CommandText = "SELECT COUNT(*) FROM trade WHERE portfolio_entry_id = $id AND is_deleted = 0";
        cmd.Parameters.AddWithValue("$id", id.ToString());
        var r = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return r is null or DBNull ? 0 : Convert.ToInt32(r, System.Globalization.CultureInfo.InvariantCulture);
    }

    // ─── IPortfolioSyncStore ────────────────────────────────────────────────

    public async Task<IReadOnlyList<SyncEnvelope>> GetPendingPushAsync(CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {SyncSelectClause} FROM portfolio WHERE is_pending_push = 1;";
        var results = new List<SyncEnvelope>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var entry = MapEntry(reader);
            var version = new EntityVersion(
                Version: reader.GetInt64(8),
                LastModifiedAt: DateTimeOffset.Parse(reader.GetString(9)),
                LastModifiedByDevice: reader.GetString(10));
            var isDeleted = reader.GetInt32(11) != 0;
            results.Add(PortfolioSyncMapper.ToEnvelope(entry, version, isDeleted));
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
        cmd.CommandText = "UPDATE portfolio SET is_pending_push = 0 WHERE id = $id;";
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
            if (env.EntityType != PortfolioSyncMapper.EntityType) continue;

            await using (var probe = conn.CreateCommand())
            {
                probe.Transaction = tx;
                probe.CommandText = "SELECT version FROM portfolio WHERE id = $id;";
                probe.Parameters.AddWithValue("$id", env.EntityId.ToString());
                var existing = await probe.ExecuteScalarAsync(ct).ConfigureAwait(false);
                if (existing is not null && Convert.ToInt64(existing) >= env.Version.Version)
                    continue;
            }

            if (env.Deleted)
            {
                // Tombstone: use GUID-derived placeholder symbol/exchange so partial unique index never collides.
                var placeholder = $"__tombstone_{env.EntityId:N}";
                await using var del = conn.CreateCommand();
                del.Transaction = tx;
                del.CommandText = """
                    INSERT INTO portfolio
                        (id, symbol, exchange, asset_type, created_at, updated_at,
                         display_name, currency, is_active, is_etf,
                         version, last_modified_at, last_modified_by_device, is_deleted, is_pending_push)
                    VALUES
                        ($id, $sym, $ex, 'Stock', $now, $now,
                         '', 'TWD', 0, 0,
                         $ver, $modAt, $modBy, 1, 0)
                    ON CONFLICT(id) DO UPDATE SET
                        is_deleted = 1,
                        is_active = 0,
                        updated_at = excluded.updated_at,
                        version = excluded.version,
                        last_modified_at = excluded.last_modified_at,
                        last_modified_by_device = excluded.last_modified_by_device,
                        is_pending_push = 0;
                    """;
                del.Parameters.AddWithValue("$id", env.EntityId.ToString());
                del.Parameters.AddWithValue("$sym", placeholder);
                del.Parameters.AddWithValue("$ex", placeholder);
                del.Parameters.AddWithValue("$now", NowIso());
                del.Parameters.AddWithValue("$ver", env.Version.Version);
                del.Parameters.AddWithValue("$modAt", env.Version.LastModifiedAt.UtcDateTime.ToString("o"));
                del.Parameters.AddWithValue("$modBy", env.Version.LastModifiedByDevice);
                await del.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                continue;
            }

            var entry = PortfolioSyncMapper.FromPayload(env);
            await using var up = conn.CreateCommand();
            up.Transaction = tx;
            up.CommandText = """
                INSERT INTO portfolio
                    (id, symbol, exchange, asset_type, created_at, updated_at,
                     display_name, currency, is_active, is_etf,
                     version, last_modified_at, last_modified_by_device, is_deleted, is_pending_push)
                VALUES
                    ($id, $sym, $ex, $at, $now, $now,
                     $dn, $cur, $ia, $etf,
                     $ver, $modAt, $modBy, 0, 0)
                ON CONFLICT(id) DO UPDATE SET
                    symbol = excluded.symbol,
                    exchange = excluded.exchange,
                    asset_type = excluded.asset_type,
                    updated_at = excluded.updated_at,
                    display_name = excluded.display_name,
                    currency = excluded.currency,
                    is_active = excluded.is_active,
                    is_etf = excluded.is_etf,
                    version = excluded.version,
                    last_modified_at = excluded.last_modified_at,
                    last_modified_by_device = excluded.last_modified_by_device,
                    is_deleted = 0,
                    is_pending_push = 0;
                """;
            BindEntry(up, entry);
            up.Parameters.AddWithValue("$now", NowIso());
            up.Parameters.AddWithValue("$ver", env.Version.Version);
            up.Parameters.AddWithValue("$modAt", env.Version.LastModifiedAt.UtcDateTime.ToString("o"));
            up.Parameters.AddWithValue("$modBy", env.Version.LastModifiedByDevice);
            await up.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await tx.CommitAsync(ct).ConfigureAwait(false);
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    private string NowIso() => _time.GetUtcNow().UtcDateTime.ToString("o");

    private string CurrentDeviceId()
    {
        var deviceId = _deviceIdProvider();
        return string.IsNullOrWhiteSpace(deviceId) ? "local" : deviceId;
    }

    private void StampSyncParams(SqliteCommand cmd)
    {
        cmd.Parameters.AddWithValue("$now", NowIso());
        cmd.Parameters.AddWithValue("$device", CurrentDeviceId());
    }

    private static void BindEntry(SqliteCommand cmd, PortfolioEntry e)
    {
        cmd.Parameters.AddWithValue("$id", e.Id.ToString());
        cmd.Parameters.AddWithValue("$sym", e.Symbol);
        cmd.Parameters.AddWithValue("$ex", e.Exchange);
        cmd.Parameters.AddWithValue("$at", e.AssetType.ToString());
        cmd.Parameters.AddWithValue("$dn", e.DisplayName);
        cmd.Parameters.AddWithValue("$cur", e.Currency);
        cmd.Parameters.AddWithValue("$ia", e.IsActive ? 1 : 0);
        cmd.Parameters.AddWithValue("$etf", e.IsEtf ? 1 : 0);
    }

    private static PortfolioEntry MapEntry(SqliteDataReader r)
    {
        var assetType = Enum.TryParse<AssetType>(r.GetString(3), out var t) ? t : AssetType.Stock;
        var isActive = r.IsDBNull(6) ? true : r.GetInt64(6) != 0;
        var isEtf = !r.IsDBNull(7) && r.GetInt64(7) != 0;
        return new PortfolioEntry(
            Guid.Parse(r.GetString(0)),
            r.GetString(1),
            r.GetString(2),
            assetType,
            r.IsDBNull(4) ? string.Empty : r.GetString(4),
            r.IsDBNull(5) ? "TWD" : r.GetString(5),
            isActive,
            isEtf);
    }

    private static async Task<IReadOnlyList<PortfolioEntry>> ReadEntriesAsync(SqliteCommand cmd, CancellationToken ct)
    {
        var results = new List<PortfolioEntry>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
            results.Add(MapEntry(r));
        return results;
    }
}
