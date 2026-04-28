using Microsoft.Data.Sqlite;
using Assetra.Core.Interfaces;
using Assetra.Core.Interfaces.Sync;
using Assetra.Core.Models;
using Assetra.Core.Models.Sync;
using Assetra.Infrastructure.Sync;

namespace Assetra.Infrastructure.Persistence;

/// <summary>
/// SQLite-backed <see cref="ICategoryRepository"/>，同時實作 <see cref="ICategorySyncStore"/>
/// 給雲端同步層用。每次本地 mutation 都會 bump <c>version</c>、寫入 <c>last_modified_*</c>、
/// 把 <c>is_pending_push</c> 設為 1（outbox flag）。<see cref="RemoveAsync"/> 為 soft delete
/// （tombstone）：保留 row、設 <c>is_deleted = 1</c>、bump version、push 後遠端才會收到刪除事件。
/// </summary>
public sealed class CategorySqliteRepository : ICategoryRepository, ICategorySyncStore
{
    private readonly string _connectionString;
    private readonly Func<string> _deviceIdProvider;
    private readonly TimeProvider _time;

    public CategorySqliteRepository(string dbPath, string deviceId = "local", TimeProvider? time = null)
        : this(dbPath, () => deviceId, time)
    {
    }

    public CategorySqliteRepository(string dbPath, Func<string> deviceIdProvider, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(deviceIdProvider);
        _connectionString = $"Data Source={dbPath}";
        _deviceIdProvider = deviceIdProvider;
        _time = time ?? TimeProvider.System;
        CategorySchemaMigrator.EnsureInitialized(_connectionString);
    }

    private const string SelectClause =
        "id, name, kind, parent_id, icon, color_hex, sort_order, is_archived";

    private const string SyncSelectClause =
        "id, name, kind, parent_id, icon, color_hex, sort_order, is_archived, " +
        "version, last_modified_at, last_modified_by_device, is_deleted";

    private string CurrentDeviceId()
    {
        var deviceId = _deviceIdProvider();
        return string.IsNullOrWhiteSpace(deviceId) ? "local" : deviceId;
    }

    private static ExpenseCategory Map(SqliteDataReader r) => new(
        Id: Guid.Parse(r.GetString(0)),
        Name: r.GetString(1),
        Kind: Enum.Parse<CategoryKind>(r.GetString(2)),
        ParentId: r.IsDBNull(3) ? null : Guid.Parse(r.GetString(3)),
        Icon: r.IsDBNull(4) ? null : r.GetString(4),
        ColorHex: r.IsDBNull(5) ? null : r.GetString(5),
        SortOrder: r.GetInt32(6),
        IsArchived: r.GetInt32(7) != 0);

    public async Task<IReadOnlyList<ExpenseCategory>> GetAllAsync(CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            $"SELECT {SelectClause} FROM expense_category " +
            "WHERE is_deleted = 0 " +
            "ORDER BY kind, sort_order, name;";
        var results = new List<ExpenseCategory>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            results.Add(Map(reader));
        return results;
    }

    public async Task<ExpenseCategory?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {SelectClause} FROM expense_category WHERE id = $id AND is_deleted = 0;";
        cmd.Parameters.AddWithValue("$id", id.ToString());
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false) ? Map(reader) : null;
    }

    public async Task AddAsync(ExpenseCategory c, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(c);
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR IGNORE INTO expense_category
                (id, name, kind, parent_id, icon, color_hex, sort_order, is_archived,
                 created_at, updated_at,
                 version, last_modified_at, last_modified_by_device, is_deleted, is_pending_push)
            VALUES
                ($id, $name, $kind, $pid, $icon, $color, $sort, $arch,
                 $now, $now,
                 1, $now, $device, 0, 1);
            """;
        Bind(cmd, c);
        cmd.Parameters.AddWithValue("$now", _time.GetUtcNow().UtcDateTime.ToString("o"));
        cmd.Parameters.AddWithValue("$device", CurrentDeviceId());
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task UpdateAsync(ExpenseCategory c, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(c);
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE expense_category SET
                name        = $name,
                kind        = $kind,
                parent_id   = $pid,
                icon        = $icon,
                color_hex   = $color,
                sort_order  = $sort,
                is_archived = $arch,
                updated_at  = $now,
                version     = version + 1,
                last_modified_at        = $now,
                last_modified_by_device = $device,
                is_pending_push         = 1
            WHERE id = $id;
            """;
        Bind(cmd, c);
        cmd.Parameters.AddWithValue("$now", _time.GetUtcNow().UtcDateTime.ToString("o"));
        cmd.Parameters.AddWithValue("$device", CurrentDeviceId());
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task RemoveAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        // Soft delete (tombstone): keep row so the deletion can be pushed to remote. Detach children
        // (set parent_id = null) so UI parent links don't dangle.
        cmd.CommandText = """
            UPDATE expense_category SET parent_id = NULL WHERE parent_id = $id;
            UPDATE expense_category SET
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
        cmd.Parameters.AddWithValue("$device", CurrentDeviceId());
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<bool> AnyAsync(CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT EXISTS(SELECT 1 FROM expense_category WHERE is_deleted = 0 LIMIT 1);";
        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return Convert.ToInt64(result) != 0;
    }

    // ── ICategorySyncStore ──────────────────────────────────────────────────

    public async Task<IReadOnlyList<SyncEnvelope>> GetPendingPushAsync(CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {SyncSelectClause} FROM expense_category WHERE is_pending_push = 1;";
        var results = new List<SyncEnvelope>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var category = Map(reader);
            var version = new EntityVersion(
                Version: reader.GetInt64(8),
                LastModifiedAt: DateTimeOffset.Parse(reader.GetString(9)),
                LastModifiedByDevice: reader.GetString(10));
            var isDeleted = reader.GetInt32(11) != 0;
            results.Add(CategorySyncMapper.ToEnvelope(category, version, isDeleted));
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
        cmd.CommandText = "UPDATE expense_category SET is_pending_push = 0 WHERE id = $id;";
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
            if (env.EntityType != CategorySyncMapper.EntityType) continue;

            await using (var probe = conn.CreateCommand())
            {
                probe.Transaction = tx;
                probe.CommandText = "SELECT version FROM expense_category WHERE id = $id;";
                probe.Parameters.AddWithValue("$id", env.EntityId.ToString());
                var existing = await probe.ExecuteScalarAsync(ct).ConfigureAwait(false);
                if (existing is not null && Convert.ToInt64(existing) >= env.Version.Version)
                    continue; // never write backwards
            }

            if (env.Deleted)
            {
                await using var del = conn.CreateCommand();
                del.Transaction = tx;
                del.CommandText = """
                    INSERT INTO expense_category
                        (id, name, kind, parent_id, icon, color_hex, sort_order, is_archived,
                         created_at, updated_at,
                         version, last_modified_at, last_modified_by_device, is_deleted, is_pending_push)
                    VALUES
                        ($id, '', 'Expense', NULL, NULL, NULL, 0, 0,
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

            var c = CategorySyncMapper.FromPayload(env);
            await using var up = conn.CreateCommand();
            up.Transaction = tx;
            up.CommandText = """
                INSERT INTO expense_category
                    (id, name, kind, parent_id, icon, color_hex, sort_order, is_archived,
                     created_at, updated_at,
                     version, last_modified_at, last_modified_by_device, is_deleted, is_pending_push)
                VALUES
                    ($id, $name, $kind, $pid, $icon, $color, $sort, $arch,
                     $now, $now,
                     $ver, $modAt, $modBy, 0, 0)
                ON CONFLICT(id) DO UPDATE SET
                    name = excluded.name,
                    kind = excluded.kind,
                    parent_id = excluded.parent_id,
                    icon = excluded.icon,
                    color_hex = excluded.color_hex,
                    sort_order = excluded.sort_order,
                    is_archived = excluded.is_archived,
                    updated_at = excluded.updated_at,
                    version = excluded.version,
                    last_modified_at = excluded.last_modified_at,
                    last_modified_by_device = excluded.last_modified_by_device,
                    is_deleted = 0,
                    is_pending_push = 0;
                """;
            Bind(up, c);
            up.Parameters.AddWithValue("$now", _time.GetUtcNow().UtcDateTime.ToString("o"));
            up.Parameters.AddWithValue("$ver", env.Version.Version);
            up.Parameters.AddWithValue("$modAt", env.Version.LastModifiedAt.UtcDateTime.ToString("o"));
            up.Parameters.AddWithValue("$modBy", env.Version.LastModifiedByDevice);
            await up.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await tx.CommitAsync(ct).ConfigureAwait(false);
    }

    private static void Bind(SqliteCommand cmd, ExpenseCategory c)
    {
        cmd.Parameters.AddWithValue("$id", c.Id.ToString());
        cmd.Parameters.AddWithValue("$name", c.Name);
        cmd.Parameters.AddWithValue("$kind", c.Kind.ToString());
        cmd.Parameters.AddWithValue("$pid", c.ParentId.HasValue ? (object)c.ParentId.Value.ToString() : DBNull.Value);
        cmd.Parameters.AddWithValue("$icon", c.Icon is not null ? (object)c.Icon : DBNull.Value);
        cmd.Parameters.AddWithValue("$color", c.ColorHex is not null ? (object)c.ColorHex : DBNull.Value);
        cmd.Parameters.AddWithValue("$sort", c.SortOrder);
        cmd.Parameters.AddWithValue("$arch", c.IsArchived ? 1 : 0);
    }
}
