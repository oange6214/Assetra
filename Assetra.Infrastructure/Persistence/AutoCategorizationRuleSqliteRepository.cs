using Microsoft.Data.Sqlite;
using Assetra.Core.Interfaces;
using Assetra.Core.Interfaces.Sync;
using Assetra.Core.Models;
using Assetra.Core.Models.Sync;
using Assetra.Infrastructure.Sync;

namespace Assetra.Infrastructure.Persistence;

/// <summary>
/// SQLite-backed <see cref="IAutoCategorizationRuleRepository"/> + <see cref="IAutoCategorizationRuleSyncStore"/>（v0.20.11）。
/// 每次本地 mutation bump version + stamp last_modified + 設 is_pending_push=1；
/// <see cref="RemoveAsync"/> 改為 soft delete（tombstone）。
/// </summary>
public sealed class AutoCategorizationRuleSqliteRepository : IAutoCategorizationRuleRepository, IAutoCategorizationRuleSyncStore
{
    private readonly string _connectionString;
    private readonly Func<string> _deviceIdProvider;
    private readonly TimeProvider _time;

    public AutoCategorizationRuleSqliteRepository(string dbPath, string deviceId = "local", TimeProvider? time = null)
        : this(dbPath, () => deviceId, time)
    {
    }

    public AutoCategorizationRuleSqliteRepository(string dbPath, Func<string> deviceIdProvider, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(deviceIdProvider);
        _connectionString = $"Data Source={dbPath}";
        _deviceIdProvider = deviceIdProvider;
        _time = time ?? TimeProvider.System;
        AutoCategorizationRuleSchemaMigrator.EnsureInitialized(_connectionString);
    }

    private const string SelectClause =
        "id, keyword_pattern, category_id, priority, is_enabled, match_case_sensitive, " +
        "name, match_field, match_type, applies_to";

    private const string SyncSelectClause =
        SelectClause + ", version, last_modified_at, last_modified_by_device, is_deleted";

    private static AutoCategorizationRule Map(SqliteDataReader r) => new(
        Id: Guid.Parse(r.GetString(0)),
        KeywordPattern: r.GetString(1),
        CategoryId: Guid.Parse(r.GetString(2)),
        Priority: r.GetInt32(3),
        IsEnabled: r.GetInt32(4) != 0,
        MatchCaseSensitive: r.GetInt32(5) != 0,
        Name: r.IsDBNull(6) ? null : r.GetString(6),
        MatchField: (AutoCategorizationMatchField)r.GetInt32(7),
        MatchType: (AutoCategorizationMatchType)r.GetInt32(8),
        AppliesTo: (AutoCategorizationScope)r.GetInt32(9));

    public async Task<IReadOnlyList<AutoCategorizationRule>> GetAllAsync(CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            $"SELECT {SelectClause} FROM auto_categorization_rule " +
            "WHERE is_deleted = 0 " +
            "ORDER BY priority, keyword_pattern;";
        var results = new List<AutoCategorizationRule>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            results.Add(Map(reader));
        return results;
    }

    public async Task<AutoCategorizationRule?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {SelectClause} FROM auto_categorization_rule WHERE id = $id AND is_deleted = 0;";
        cmd.Parameters.AddWithValue("$id", id.ToString());
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false) ? Map(reader) : null;
    }

    public async Task AddAsync(AutoCategorizationRule rule, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(rule);
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR IGNORE INTO auto_categorization_rule
                (id, keyword_pattern, category_id, priority, is_enabled,
                 match_case_sensitive, created_at, updated_at,
                 name, match_field, match_type, applies_to,
                 version, last_modified_at, last_modified_by_device, is_deleted, is_pending_push)
            VALUES
                ($id, $kw, $cat, $pri, $en,
                 $cs, $now, $now,
                 $name, $field, $type, $scope,
                 1, $now, $device, 0, 1);
            """;
        Bind(cmd, rule);
        StampSync(cmd);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task UpdateAsync(AutoCategorizationRule rule, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(rule);
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE auto_categorization_rule SET
                keyword_pattern      = $kw,
                category_id          = $cat,
                priority             = $pri,
                is_enabled           = $en,
                match_case_sensitive = $cs,
                name                 = $name,
                match_field          = $field,
                match_type           = $type,
                applies_to           = $scope,
                updated_at           = $now,
                version              = version + 1,
                last_modified_at        = $now,
                last_modified_by_device = $device,
                is_pending_push         = 1
            WHERE id = $id;
            """;
        Bind(cmd, rule);
        StampSync(cmd);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task RemoveAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE auto_categorization_rule SET
                is_deleted = 1,
                version    = version + 1,
                updated_at = $now,
                last_modified_at        = $now,
                last_modified_by_device = $device,
                is_pending_push         = 1
            WHERE id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", id.ToString());
        StampSync(cmd);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    // ── IAutoCategorizationRuleSyncStore ────────────────────────────────────

    public async Task<IReadOnlyList<SyncEnvelope>> GetPendingPushAsync(CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {SyncSelectClause} FROM auto_categorization_rule WHERE is_pending_push = 1;";
        var results = new List<SyncEnvelope>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var rule = Map(reader);
            var version = new EntityVersion(
                Version: reader.GetInt64(10),
                LastModifiedAt: DateTimeOffset.Parse(reader.GetString(11)),
                LastModifiedByDevice: reader.GetString(12));
            var isDeleted = reader.GetInt32(13) != 0;
            results.Add(AutoCategorizationRuleSyncMapper.ToEnvelope(rule, version, isDeleted));
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
        cmd.CommandText = "UPDATE auto_categorization_rule SET is_pending_push = 0 WHERE id = $id;";
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
            if (env.EntityType != AutoCategorizationRuleSyncMapper.EntityType) continue;

            await using (var probe = conn.CreateCommand())
            {
                probe.Transaction = tx;
                probe.CommandText = "SELECT version FROM auto_categorization_rule WHERE id = $id;";
                probe.Parameters.AddWithValue("$id", env.EntityId.ToString());
                var existing = await probe.ExecuteScalarAsync(ct).ConfigureAwait(false);
                if (existing is not null && Convert.ToInt64(existing) >= env.Version.Version) continue;
            }

            if (env.Deleted)
            {
                await using var del = conn.CreateCommand();
                del.Transaction = tx;
                del.CommandText = """
                    INSERT INTO auto_categorization_rule
                        (id, keyword_pattern, category_id, priority, is_enabled,
                         match_case_sensitive, created_at, updated_at,
                         name, match_field, match_type, applies_to,
                         version, last_modified_at, last_modified_by_device, is_deleted, is_pending_push)
                    VALUES
                        ($id, '', $catPlaceholder, 0, 0,
                         0, $now, $now,
                         NULL, 3, 0, 3,
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
                del.Parameters.AddWithValue("$catPlaceholder", Guid.Empty.ToString());
                del.Parameters.AddWithValue("$now", NowIso());
                del.Parameters.AddWithValue("$ver", env.Version.Version);
                del.Parameters.AddWithValue("$modAt", env.Version.LastModifiedAt.UtcDateTime.ToString("o"));
                del.Parameters.AddWithValue("$modBy", env.Version.LastModifiedByDevice);
                await del.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                continue;
            }

            var rule = AutoCategorizationRuleSyncMapper.FromPayload(env);
            await using var up = conn.CreateCommand();
            up.Transaction = tx;
            up.CommandText = """
                INSERT INTO auto_categorization_rule
                    (id, keyword_pattern, category_id, priority, is_enabled,
                     match_case_sensitive, created_at, updated_at,
                     name, match_field, match_type, applies_to,
                     version, last_modified_at, last_modified_by_device, is_deleted, is_pending_push)
                VALUES
                    ($id, $kw, $cat, $pri, $en,
                     $cs, $now, $now,
                     $name, $field, $type, $scope,
                     $ver, $modAt, $modBy, 0, 0)
                ON CONFLICT(id) DO UPDATE SET
                    keyword_pattern      = excluded.keyword_pattern,
                    category_id          = excluded.category_id,
                    priority             = excluded.priority,
                    is_enabled           = excluded.is_enabled,
                    match_case_sensitive = excluded.match_case_sensitive,
                    name                 = excluded.name,
                    match_field          = excluded.match_field,
                    match_type           = excluded.match_type,
                    applies_to           = excluded.applies_to,
                    updated_at           = excluded.updated_at,
                    version              = excluded.version,
                    last_modified_at     = excluded.last_modified_at,
                    last_modified_by_device = excluded.last_modified_by_device,
                    is_deleted = 0,
                    is_pending_push = 0;
                """;
            Bind(up, rule);
            up.Parameters.AddWithValue("$now", NowIso());
            up.Parameters.AddWithValue("$ver", env.Version.Version);
            up.Parameters.AddWithValue("$modAt", env.Version.LastModifiedAt.UtcDateTime.ToString("o"));
            up.Parameters.AddWithValue("$modBy", env.Version.LastModifiedByDevice);
            await up.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await tx.CommitAsync(ct).ConfigureAwait(false);
    }

    private string NowIso() => _time.GetUtcNow().UtcDateTime.ToString("o");

    private string CurrentDeviceId()
    {
        var deviceId = _deviceIdProvider();
        return string.IsNullOrWhiteSpace(deviceId) ? "local" : deviceId;
    }

    private void StampSync(SqliteCommand cmd)
    {
        cmd.Parameters.AddWithValue("$now", NowIso());
        cmd.Parameters.AddWithValue("$device", CurrentDeviceId());
    }

    private static void Bind(SqliteCommand cmd, AutoCategorizationRule r)
    {
        cmd.Parameters.AddWithValue("$id", r.Id.ToString());
        cmd.Parameters.AddWithValue("$kw", r.KeywordPattern);
        cmd.Parameters.AddWithValue("$cat", r.CategoryId.ToString());
        cmd.Parameters.AddWithValue("$pri", r.Priority);
        cmd.Parameters.AddWithValue("$en", r.IsEnabled ? 1 : 0);
        cmd.Parameters.AddWithValue("$cs", r.MatchCaseSensitive ? 1 : 0);
        cmd.Parameters.AddWithValue("$name", (object?)r.Name ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$field", (int)r.MatchField);
        cmd.Parameters.AddWithValue("$type", (int)r.MatchType);
        cmd.Parameters.AddWithValue("$scope", (int)r.AppliesTo);
    }
}
