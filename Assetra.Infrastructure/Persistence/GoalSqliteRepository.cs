using Assetra.Core.Interfaces;
using Assetra.Core.Interfaces.Sync;
using Assetra.Core.Models;
using Assetra.Core.Models.Sync;
using Assetra.Infrastructure.Sync;
using Microsoft.Data.Sqlite;

namespace Assetra.Infrastructure.Persistence;

/// <summary>
/// SQLite-backed <see cref="IFinancialGoalRepository"/> + <see cref="IFinancialGoalSyncStore"/>.
/// Sync columns added in the Sync-Goal-PortfolioGroup pass — mirror the
/// pattern used by Category / Trade / Asset repos. RemoveAsync became a soft
/// delete (is_deleted=1) so deletion can be pushed as a tombstone.
/// </summary>
public sealed class GoalSqliteRepository : IFinancialGoalRepository, IFinancialGoalSyncStore
{
    private readonly string _connectionString;
    private readonly Func<string> _deviceIdProvider;
    private readonly TimeProvider _time;

    private const string SelectClause =
        "id, name, target_amount, current_amount, deadline, notes, linked_asset_class, portfolio_group_id";
    private const string SyncSelectClause = SelectClause +
        ", version, last_modified_at, last_modified_by_device, is_deleted";

    public GoalSqliteRepository(string dbPath, string deviceId = "local", TimeProvider? time = null)
        : this(dbPath, () => deviceId, time)
    {
    }

    public GoalSqliteRepository(string dbPath, Func<string> deviceIdProvider, TimeProvider? time = null)
    {
        _connectionString = $"Data Source={dbPath}";
        _deviceIdProvider = deviceIdProvider ?? throw new ArgumentNullException(nameof(deviceIdProvider));
        _time = time ?? TimeProvider.System;
        GoalSchemaMigrator.EnsureInitialized(_connectionString);
    }

    private string CurrentDeviceId()
    {
        var d = _deviceIdProvider();
        return string.IsNullOrWhiteSpace(d) ? "local" : d;
    }

    public async Task<IReadOnlyList<FinancialGoal>> GetAllAsync(CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {SelectClause} FROM financial_goal WHERE is_deleted = 0 ORDER BY rowid;";
        var results = new List<FinancialGoal>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            results.Add(MapGoal(reader));
        return results;
    }

    public async Task AddAsync(FinancialGoal goal, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO financial_goal
                (id, name, target_amount, current_amount, deadline, notes, linked_asset_class, portfolio_group_id,
                 created_at, updated_at,
                 version, last_modified_at, last_modified_by_device, is_deleted, is_pending_push)
            VALUES
                ($id, $name, $target, $current, $deadline, $notes, $linked_asset_class, $portfolio_group_id,
                 $now, $now,
                 1, $now, $device, 0, 1);
            """;
        BindGoalParams(cmd, goal);
        StampSyncParams(cmd);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task UpdateAsync(FinancialGoal goal, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE financial_goal SET
                name                    = $name,
                target_amount           = $target,
                current_amount          = $current,
                deadline                = $deadline,
                notes                   = $notes,
                linked_asset_class      = $linked_asset_class,
                portfolio_group_id      = $portfolio_group_id,
                updated_at              = $now,
                version                 = version + 1,
                last_modified_at        = $now,
                last_modified_by_device = $device,
                is_pending_push         = 1
            WHERE id = $id;
            """;
        BindGoalParams(cmd, goal);
        StampSyncParams(cmd);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task RemoveAsync(Guid id, CancellationToken ct = default)
    {
        // Soft delete tombstone on the goal row (sync propagation), plus a
        // hard delete on local-only milestone + funding_rule rows so the
        // legacy cascade semantics still hold for the UI/local data.
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct).ConfigureAwait(false);

        await using (var ms = conn.CreateCommand())
        {
            ms.Transaction = tx;
            ms.CommandText = "DELETE FROM goal_milestone WHERE goal_id = $id;";
            ms.Parameters.AddWithValue("$id", id.ToString());
            await ms.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        await using (var rules = conn.CreateCommand())
        {
            rules.Transaction = tx;
            rules.CommandText = "DELETE FROM goal_funding_rule WHERE goal_id = $id;";
            rules.Parameters.AddWithValue("$id", id.ToString());
            await rules.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await using (var goalDel = conn.CreateCommand())
        {
            goalDel.Transaction = tx;
            goalDel.CommandText = """
                UPDATE financial_goal SET
                    is_deleted              = 1,
                    version                 = version + 1,
                    updated_at              = $now,
                    last_modified_at        = $now,
                    last_modified_by_device = $device,
                    is_pending_push         = 1
                WHERE id = $id;
                """;
            goalDel.Parameters.AddWithValue("$id", id.ToString());
            StampSyncParams(goalDel);
            await goalDel.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await tx.CommitAsync(ct).ConfigureAwait(false);
    }

    private static FinancialGoal MapGoal(SqliteDataReader r)
    {
        DateOnly? deadline = r.IsDBNull(4) ? null : DateOnly.Parse(r.GetString(4));
        return new FinancialGoal(
            Guid.Parse(r.GetString(0)),
            r.GetString(1),
            (decimal)r.GetDouble(2),
            (decimal)r.GetDouble(3),
            deadline,
            r.IsDBNull(5) ? null : r.GetString(5),
            r.IsDBNull(6) ? null : r.GetString(6),
            r.IsDBNull(7) ? null : Guid.Parse(r.GetString(7)));
    }

    private static void BindGoalParams(SqliteCommand cmd, FinancialGoal goal)
    {
        cmd.Parameters.AddWithValue("$id", goal.Id.ToString());
        cmd.Parameters.AddWithValue("$name", goal.Name);
        cmd.Parameters.AddWithValue("$target", (double)goal.TargetAmount);
        cmd.Parameters.AddWithValue("$current", (double)goal.CurrentAmount);
        cmd.Parameters.AddWithValue("$deadline",
            goal.Deadline?.ToString("yyyy-MM-dd") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$notes", goal.Notes ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$linked_asset_class",
            string.IsNullOrWhiteSpace(goal.LinkedAssetClass) ? (object)DBNull.Value : goal.LinkedAssetClass);
        cmd.Parameters.AddWithValue("$portfolio_group_id",
            goal.PortfolioGroupId.HasValue ? (object)goal.PortfolioGroupId.Value.ToString() : DBNull.Value);
    }

    private void StampSyncParams(SqliteCommand cmd)
    {
        cmd.Parameters.AddWithValue("$now", _time.GetUtcNow().UtcDateTime.ToString("o"));
        cmd.Parameters.AddWithValue("$device", CurrentDeviceId());
    }

    // ── IFinancialGoalSyncStore ─────────────────────────────────────────────

    public async Task<IReadOnlyList<SyncEnvelope>> GetPendingPushAsync(CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {SyncSelectClause} FROM financial_goal WHERE is_pending_push = 1;";
        var results = new List<SyncEnvelope>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var goal = MapGoal(reader);
            // SelectClause has 8 cols → sync metadata starts at ordinal 8.
            var version = new EntityVersion(
                Version: reader.GetInt64(8),
                LastModifiedAt: DateTimeOffset.Parse(reader.GetString(9)),
                LastModifiedByDevice: reader.GetString(10));
            var isDeleted = reader.GetInt32(11) != 0;
            results.Add(FinancialGoalSyncMapper.ToEnvelope(goal, version, isDeleted));
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
        cmd.CommandText = "UPDATE financial_goal SET is_pending_push = 0 WHERE id = $id;";
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
            if (env.EntityType != FinancialGoalSyncMapper.EntityType)
                continue;

            await using (var probe = conn.CreateCommand())
            {
                probe.Transaction = tx;
                probe.CommandText = "SELECT version FROM financial_goal WHERE id = $id;";
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
                    INSERT INTO financial_goal
                        (id, name, target_amount, current_amount, deadline, notes,
                         linked_asset_class, portfolio_group_id,
                         created_at, updated_at,
                         version, last_modified_at, last_modified_by_device, is_deleted, is_pending_push)
                    VALUES
                        ($id, '', 0, 0, NULL, NULL,
                         NULL, NULL,
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

            var g = FinancialGoalSyncMapper.FromPayload(env);
            await using var up = conn.CreateCommand();
            up.Transaction = tx;
            up.CommandText = """
                INSERT INTO financial_goal
                    (id, name, target_amount, current_amount, deadline, notes,
                     linked_asset_class, portfolio_group_id,
                     created_at, updated_at,
                     version, last_modified_at, last_modified_by_device, is_deleted, is_pending_push)
                VALUES
                    ($id, $name, $target, $current, $deadline, $notes,
                     $linked_asset_class, $portfolio_group_id,
                     $now, $now,
                     $ver, $modAt, $modBy, 0, 0)
                ON CONFLICT(id) DO UPDATE SET
                    name = excluded.name,
                    target_amount = excluded.target_amount,
                    current_amount = excluded.current_amount,
                    deadline = excluded.deadline,
                    notes = excluded.notes,
                    linked_asset_class = excluded.linked_asset_class,
                    portfolio_group_id = excluded.portfolio_group_id,
                    updated_at = excluded.updated_at,
                    version = excluded.version,
                    last_modified_at = excluded.last_modified_at,
                    last_modified_by_device = excluded.last_modified_by_device,
                    is_deleted = 0,
                    is_pending_push = 0;
                """;
            BindGoalParams(up, g);
            up.Parameters.AddWithValue("$now", _time.GetUtcNow().UtcDateTime.ToString("o"));
            up.Parameters.AddWithValue("$ver", env.Version.Version);
            up.Parameters.AddWithValue("$modAt", env.Version.LastModifiedAt.UtcDateTime.ToString("o"));
            up.Parameters.AddWithValue("$modBy", env.Version.LastModifiedByDevice);
            await up.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await tx.CommitAsync(ct).ConfigureAwait(false);
    }
}
