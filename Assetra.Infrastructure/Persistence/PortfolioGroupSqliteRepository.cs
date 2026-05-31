using Assetra.Core.Interfaces;
using Assetra.Core.Interfaces.Sync;
using Assetra.Core.Models;
using Assetra.Core.Models.Sync;
using Assetra.Infrastructure.Sync;
using Microsoft.Data.Sqlite;

namespace Assetra.Infrastructure.Persistence;

/// <summary>
/// SQLite-backed <see cref="IPortfolioGroupRepository"/> + <see cref="IPortfolioGroupSyncStore"/>.
/// Soft-delete tombstones on Remove so deletions propagate over sync. The
/// system-protected guard runs BEFORE the soft delete write, so the default
/// group still can't be deleted even via sync.
/// </summary>
public sealed class PortfolioGroupSqliteRepository : IPortfolioGroupRepository, IPortfolioGroupSyncStore
{
    private readonly string _connectionString;
    private readonly Func<string> _deviceIdProvider;
    private readonly TimeProvider _time;

    private const string SelectClause =
        "id, name, color_hex, description, icon_key, sort_order, default_cash_account_id, is_system";
    private const string SyncSelectClause = SelectClause +
        ", version, last_modified_at, last_modified_by_device, is_deleted";

    public PortfolioGroupSqliteRepository(string dbPath, string deviceId = "local", TimeProvider? time = null)
        : this(dbPath, () => deviceId, time)
    {
    }

    public PortfolioGroupSqliteRepository(string dbPath, Func<string> deviceIdProvider, TimeProvider? time = null)
    {
        _connectionString = $"Data Source={dbPath}";
        _deviceIdProvider = deviceIdProvider ?? throw new ArgumentNullException(nameof(deviceIdProvider));
        _time = time ?? TimeProvider.System;
        PortfolioGroupSchemaMigrator.EnsureInitialized(_connectionString);
    }

    private string CurrentDeviceId()
    {
        var d = _deviceIdProvider();
        return string.IsNullOrWhiteSpace(d) ? "local" : d;
    }

    public async Task<IReadOnlyList<PortfolioGroup>> GetAllAsync(CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {SelectClause} FROM portfolio_group WHERE is_deleted = 0 ORDER BY sort_order, created_at;";
        var results = new List<PortfolioGroup>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            results.Add(MapGroup(reader));
        return results;
    }

    public async Task<PortfolioGroup?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {SelectClause} FROM portfolio_group WHERE id = $id AND is_deleted = 0;";
        cmd.Parameters.AddWithValue("$id", id.ToString());
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false) ? MapGroup(reader) : null;
    }

    public async Task AddAsync(PortfolioGroup group, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(group);
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO portfolio_group
                (id, name, color_hex, description, icon_key, sort_order, default_cash_account_id, is_system,
                 created_at, updated_at,
                 version, last_modified_at, last_modified_by_device, is_deleted, is_pending_push)
            VALUES
                ($id, $name, $color, $desc, $icon, $sort, $cash, $sys,
                 $now, $now,
                 1, $now, $device, 0, 1);
            """;
        BindGroupParams(cmd, group);
        StampSyncParams(cmd);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task UpdateAsync(PortfolioGroup group, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(group);
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE portfolio_group SET
                name                    = $name,
                color_hex               = $color,
                description             = $desc,
                icon_key                = $icon,
                sort_order              = $sort,
                default_cash_account_id = $cash,
                updated_at              = $now,
                version                 = version + 1,
                last_modified_at        = $now,
                last_modified_by_device = $device,
                is_pending_push         = 1
            WHERE id = $id;
            """;
        BindGroupParams(cmd, group);
        StampSyncParams(cmd);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task RemoveAsync(Guid id, CancellationToken ct = default)
    {
        var existing = await GetByIdAsync(id, ct).ConfigureAwait(false);
        if (existing is null)
            return;
        if (existing.IsSystem)
            throw new InvalidOperationException($"Portfolio group '{existing.Name}' is system-protected and cannot be deleted.");

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        // Soft delete + tombstone for sync — was hard DELETE before.
        cmd.CommandText = """
            UPDATE portfolio_group SET
                is_deleted              = 1,
                version                 = version + 1,
                updated_at              = $now,
                last_modified_at        = $now,
                last_modified_by_device = $device,
                is_pending_push         = 1
            WHERE id = $id AND is_system = 0;
            """;
        cmd.Parameters.AddWithValue("$id", id.ToString());
        StampSyncParams(cmd);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static PortfolioGroup MapGroup(SqliteDataReader r) => new(
        Id: Guid.Parse(r.GetString(0)),
        Name: r.GetString(1),
        ColorHex: r.IsDBNull(2) ? null : r.GetString(2),
        Description: r.IsDBNull(3) ? null : r.GetString(3),
        IconKey: r.IsDBNull(4) ? null : r.GetString(4),
        SortOrder: r.GetInt32(5),
        DefaultCashAccountId: r.IsDBNull(6) ? null : Guid.Parse(r.GetString(6)),
        IsSystem: r.GetInt32(7) != 0);

    private static void BindGroupParams(SqliteCommand cmd, PortfolioGroup g)
    {
        cmd.Parameters.AddWithValue("$id", g.Id.ToString());
        cmd.Parameters.AddWithValue("$name", g.Name);
        cmd.Parameters.AddWithValue("$color", (object?)g.ColorHex ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$desc", (object?)g.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$icon", (object?)g.IconKey ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$sort", g.SortOrder);
        cmd.Parameters.AddWithValue("$cash",
            g.DefaultCashAccountId.HasValue ? (object)g.DefaultCashAccountId.Value.ToString() : DBNull.Value);
        cmd.Parameters.AddWithValue("$sys", g.IsSystem ? 1 : 0);
    }

    private void StampSyncParams(SqliteCommand cmd)
    {
        cmd.Parameters.AddWithValue("$now", _time.GetUtcNow().UtcDateTime.ToString("o"));
        cmd.Parameters.AddWithValue("$device", CurrentDeviceId());
    }

    // ── IPortfolioGroupSyncStore ──────────────────────────────────────────

    public async Task<IReadOnlyList<SyncEnvelope>> GetPendingPushAsync(CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {SyncSelectClause} FROM portfolio_group WHERE is_pending_push = 1;";
        var results = new List<SyncEnvelope>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var group = MapGroup(reader);
            // SelectClause has 8 cols → sync metadata starts at ordinal 8.
            var version = new EntityVersion(
                Version: reader.GetInt64(8),
                LastModifiedAt: DateTimeOffset.Parse(reader.GetString(9)),
                LastModifiedByDevice: reader.GetString(10));
            var isDeleted = reader.GetInt32(11) != 0;
            results.Add(PortfolioGroupSyncMapper.ToEnvelope(group, version, isDeleted));
        }
        return results;
    }

    public async Task MarkPushedAsync(IReadOnlyList<Guid> ids, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.Count == 0)
            return;
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "UPDATE portfolio_group SET is_pending_push = 0 WHERE id = $id;";
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
        if (envelopes.Count == 0)
            return;
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct).ConfigureAwait(false);

        foreach (var env in envelopes)
        {
            if (env.EntityType != PortfolioGroupSyncMapper.EntityType)
                continue;

            await using (var probe = conn.CreateCommand())
            {
                probe.Transaction = tx;
                probe.CommandText = "SELECT version, is_system FROM portfolio_group WHERE id = $id;";
                probe.Parameters.AddWithValue("$id", env.EntityId.ToString());
                await using var pr = await probe.ExecuteReaderAsync(ct).ConfigureAwait(false);
                if (await pr.ReadAsync(ct).ConfigureAwait(false))
                {
                    var existingVer = pr.GetInt64(0);
                    var existingSystem = pr.GetInt32(1) != 0;
                    if (existingVer >= env.Version.Version)
                        continue; // backwards
                    // Defense: if local row is system-protected, ignore remote
                    // tombstone — prevents another device from deleting our default group.
                    if (existingSystem && env.Deleted)
                        continue;
                }
            }

            if (env.Deleted)
            {
                await using var del = conn.CreateCommand();
                del.Transaction = tx;
                del.CommandText = """
                    INSERT INTO portfolio_group
                        (id, name, color_hex, description, icon_key, sort_order,
                         default_cash_account_id, is_system,
                         created_at, updated_at,
                         version, last_modified_at, last_modified_by_device, is_deleted, is_pending_push)
                    VALUES
                        ($id, '', NULL, NULL, NULL, 0,
                         NULL, 0,
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

            var g = PortfolioGroupSyncMapper.FromPayload(env);
            await using var up = conn.CreateCommand();
            up.Transaction = tx;
            up.CommandText = """
                INSERT INTO portfolio_group
                    (id, name, color_hex, description, icon_key, sort_order,
                     default_cash_account_id, is_system,
                     created_at, updated_at,
                     version, last_modified_at, last_modified_by_device, is_deleted, is_pending_push)
                VALUES
                    ($id, $name, $color, $desc, $icon, $sort,
                     $cash, $sys,
                     $now, $now,
                     $ver, $modAt, $modBy, 0, 0)
                ON CONFLICT(id) DO UPDATE SET
                    name = excluded.name,
                    color_hex = excluded.color_hex,
                    description = excluded.description,
                    icon_key = excluded.icon_key,
                    sort_order = excluded.sort_order,
                    default_cash_account_id = excluded.default_cash_account_id,
                    -- is_system intentionally NOT updated from remote — local seed wins
                    updated_at = excluded.updated_at,
                    version = excluded.version,
                    last_modified_at = excluded.last_modified_at,
                    last_modified_by_device = excluded.last_modified_by_device,
                    is_deleted = 0,
                    is_pending_push = 0;
                """;
            BindGroupParams(up, g);
            up.Parameters.AddWithValue("$now", _time.GetUtcNow().UtcDateTime.ToString("o"));
            up.Parameters.AddWithValue("$ver", env.Version.Version);
            up.Parameters.AddWithValue("$modAt", env.Version.LastModifiedAt.UtcDateTime.ToString("o"));
            up.Parameters.AddWithValue("$modBy", env.Version.LastModifiedByDevice);
            await up.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await tx.CommitAsync(ct).ConfigureAwait(false);
    }
}
