using Assetra.Core.Interfaces;
using Assetra.Core.Interfaces.Sync;
using Assetra.Core.Models;
using Assetra.Core.Models.Sync;
using Assetra.Infrastructure.Sync;
using Microsoft.Data.Sqlite;

namespace Assetra.Infrastructure.Persistence;

/// <summary>
/// AlertRule SQLite 持久化 + 雲端同步存取點。
/// 跟 RetirementAccountSqliteRepository 對稱：同一個物件同時實作
/// <see cref="IAlertRepository"/> 與 <see cref="IAlertSyncStore"/>。
/// </summary>
public sealed class AlertSqliteRepository : IAlertRepository, IAlertSyncStore
{
    private readonly string _connectionString;
    private readonly Func<string> _deviceIdProvider;
    private readonly TimeProvider _time;

    public AlertSqliteRepository(string dbPath)
        : this(dbPath, () => "local", TimeProvider.System)
    {
    }

    public AlertSqliteRepository(string dbPath, Func<string> deviceIdProvider, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(deviceIdProvider);
        _connectionString = $"Data Source={dbPath}";
        _deviceIdProvider = deviceIdProvider;
        _time = time ?? TimeProvider.System;
        AlertSchemaMigrator.EnsureInitialized(_connectionString);
    }

    public async Task<IReadOnlyList<AlertRule>> GetRulesAsync(CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, symbol, exchange, condition, target_price, is_triggered, trigger_time,
                   ev_version, ev_modified_at, ev_device_id
            FROM alert
            WHERE is_deleted = 0
            ORDER BY rowid;
            """;
        var results = new List<AlertRule>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            results.Add(MapRow(reader));
        return results;
    }

    public async Task AddAsync(AlertRule rule, CancellationToken ct = default)
    {
        var stamped = StampLocalMutation(rule);
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR IGNORE INTO alert
                (id, symbol, exchange, condition, target_price, is_triggered, trigger_time,
                 ev_version, ev_modified_at, ev_device_id,
                 is_deleted, is_pending_push, created_at, updated_at)
            VALUES
                ($id, $sym, $ex, $cond, $tp, $trig, $tt,
                 $ev_version, $ev_modified_at, $ev_device_id,
                 0, 1, $now, $now);
            """;
        BindParams(cmd, stamped);
        cmd.Parameters.AddWithValue("$now", stamped.Version.LastModifiedAt.UtcDateTime.ToString("o"));
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task RemoveAsync(Guid id, CancellationToken ct = default)
    {
        // 軟刪除：is_deleted=1 + 重新 stamp version。同步推上去後雲端會看到 tombstone。
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE alert SET
                is_deleted      = 1,
                is_pending_push = 1,
                ev_version      = ev_version + 1,
                ev_modified_at  = $modified_at,
                ev_device_id    = $device,
                updated_at      = $now
            WHERE id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", id.ToString());
        var now = _time.GetUtcNow();
        cmd.Parameters.AddWithValue("$modified_at", now.ToString("o"));
        cmd.Parameters.AddWithValue("$device", CurrentDeviceId());
        cmd.Parameters.AddWithValue("$now", now.UtcDateTime.ToString("o"));
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task UpdateAsync(AlertRule rule, CancellationToken ct = default)
    {
        var stamped = StampLocalMutation(rule);
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE alert SET
                condition       = $cond,
                target_price    = $tp,
                is_triggered    = $trig,
                trigger_time    = $tt,
                ev_version      = $ev_version,
                ev_modified_at  = $ev_modified_at,
                ev_device_id    = $ev_device_id,
                is_pending_push = 1,
                updated_at      = $now
            WHERE id = $id;
            """;
        BindParams(cmd, stamped);
        cmd.Parameters.AddWithValue("$now", stamped.Version.LastModifiedAt.UtcDateTime.ToString("o"));
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    // ───── IAlertSyncStore ─────

    public async Task<IReadOnlyList<SyncEnvelope>> GetPendingPushAsync(CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, symbol, exchange, condition, target_price, is_triggered, trigger_time,
                   ev_version, ev_modified_at, ev_device_id, is_deleted
            FROM alert WHERE is_pending_push = 1;
            """;
        var results = new List<SyncEnvelope>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var version = ReadVersion(reader, 7);
            var entity = MapRowFull(reader, version);
            var isDeleted = reader.GetInt64(10) != 0;
            results.Add(AlertSyncMapper.ToEnvelope(entity, isDeleted));
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
        cmd.CommandText = "UPDATE alert SET is_pending_push = 0 WHERE id = $id;";
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
            if (env.EntityType != AlertSyncMapper.EntityType)
                continue;

            // version-比較：本地 >= remote 跳過（last-write-wins by version 編號）。
            await using (var probe = conn.CreateCommand())
            {
                probe.Transaction = tx;
                probe.CommandText = "SELECT ev_version FROM alert WHERE id = $id;";
                probe.Parameters.AddWithValue("$id", env.EntityId.ToString());
                var existing = await probe.ExecuteScalarAsync(ct).ConfigureAwait(false);
                if (existing is not null && Convert.ToInt64(existing) >= env.Version.Version)
                    continue;
            }

            if (env.Deleted)
            {
                await using var del = conn.CreateCommand();
                del.Transaction = tx;
                del.CommandText = """
                    INSERT INTO alert
                        (id, symbol, exchange, condition, target_price, is_triggered, trigger_time,
                         ev_version, ev_modified_at, ev_device_id,
                         is_deleted, is_pending_push, created_at, updated_at)
                    VALUES
                        ($id, '', '', 0, 0, 0, NULL,
                         $ev_version, $ev_modified_at, $ev_device_id,
                         1, 0, $now, $now)
                    ON CONFLICT(id) DO UPDATE SET
                        is_deleted      = 1,
                        ev_version      = excluded.ev_version,
                        ev_modified_at  = excluded.ev_modified_at,
                        ev_device_id    = excluded.ev_device_id,
                        is_pending_push = 0,
                        updated_at      = excluded.updated_at;
                    """;
                del.Parameters.AddWithValue("$id", env.EntityId.ToString());
                del.Parameters.AddWithValue("$ev_version", env.Version.Version);
                del.Parameters.AddWithValue("$ev_modified_at", env.Version.LastModifiedAt.ToString("o"));
                del.Parameters.AddWithValue("$ev_device_id", env.Version.LastModifiedByDevice);
                del.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
                await del.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
            else
            {
                var entity = AlertSyncMapper.FromPayload(env, env.Version);
                await using var upsert = conn.CreateCommand();
                upsert.Transaction = tx;
                upsert.CommandText = """
                    INSERT INTO alert
                        (id, symbol, exchange, condition, target_price, is_triggered, trigger_time,
                         ev_version, ev_modified_at, ev_device_id,
                         is_deleted, is_pending_push, created_at, updated_at)
                    VALUES
                        ($id, $sym, $ex, $cond, $tp, $trig, $tt,
                         $ev_version, $ev_modified_at, $ev_device_id,
                         0, 0, $now, $now)
                    ON CONFLICT(id) DO UPDATE SET
                        symbol          = excluded.symbol,
                        exchange        = excluded.exchange,
                        condition       = excluded.condition,
                        target_price    = excluded.target_price,
                        is_triggered    = excluded.is_triggered,
                        trigger_time    = excluded.trigger_time,
                        ev_version      = excluded.ev_version,
                        ev_modified_at  = excluded.ev_modified_at,
                        ev_device_id    = excluded.ev_device_id,
                        is_deleted      = 0,
                        is_pending_push = 0,
                        updated_at      = excluded.updated_at;
                    """;
                BindParams(upsert, entity);
                upsert.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
                await upsert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
        }
        await tx.CommitAsync(ct).ConfigureAwait(false);
    }

    // ───── Helpers ─────

    private static AlertRule MapRow(SqliteDataReader r)
    {
        var version = ReadVersion(r, 7);
        return MapRowFull(r, version);
    }

    private static AlertRule MapRowFull(SqliteDataReader r, EntityVersion version) =>
        new(
            Id: Guid.Parse(r.GetString(0)),
            Symbol: r.GetString(1),
            Exchange: r.GetString(2),
            Condition: (AlertCondition)r.GetInt32(3),
            TargetPrice: (decimal)r.GetDouble(4),
            IsTriggered: r.GetInt32(5) != 0,
            TriggerTime: r.IsDBNull(6) ? null : DateTimeOffset.Parse(r.GetString(6)))
        {
            Version = version,
        };

    private static EntityVersion ReadVersion(SqliteDataReader r, int versionOffset) =>
        new(
            r.GetInt64(versionOffset),
            r.IsDBNull(versionOffset + 1) || r.GetString(versionOffset + 1).Length == 0
                ? default
                : DateTimeOffset.Parse(r.GetString(versionOffset + 1)),
            r.IsDBNull(versionOffset + 2) ? string.Empty : r.GetString(versionOffset + 2));

    private static void BindParams(SqliteCommand cmd, AlertRule e)
    {
        cmd.Parameters.AddWithValue("$id", e.Id.ToString());
        cmd.Parameters.AddWithValue("$sym", e.Symbol);
        cmd.Parameters.AddWithValue("$ex", e.Exchange);
        cmd.Parameters.AddWithValue("$cond", (int)e.Condition);
        cmd.Parameters.AddWithValue("$tp", (double)e.TargetPrice);
        cmd.Parameters.AddWithValue("$trig", e.IsTriggered ? 1 : 0);
        cmd.Parameters.AddWithValue("$tt", e.TriggerTime?.ToString("o") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$ev_version", e.Version.Version);
        cmd.Parameters.AddWithValue("$ev_modified_at", e.Version.LastModifiedAt == default
            ? string.Empty : e.Version.LastModifiedAt.ToString("o"));
        cmd.Parameters.AddWithValue("$ev_device_id", e.Version.LastModifiedByDevice);
    }

    private AlertRule StampLocalMutation(AlertRule entity)
    {
        var now = _time.GetUtcNow();
        var deviceId = CurrentDeviceId();
        // 第一次寫入時 Version=0 → bump 到 1；後續 update 也 +1。
        var nextVersion = entity.Version.Version == 0
            ? EntityVersion.Initial(deviceId, now)
            : entity.Version.Bump(deviceId, now);
        return entity with { Version = nextVersion };
    }

    private string CurrentDeviceId()
    {
        var device = _deviceIdProvider();
        return string.IsNullOrWhiteSpace(device) ? "local" : device;
    }
}
