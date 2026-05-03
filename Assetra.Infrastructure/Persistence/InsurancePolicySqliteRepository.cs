using Assetra.Core.Interfaces.MultiAsset;
using Assetra.Core.Interfaces.Sync;
using Assetra.Core.Models.MultiAsset;
using Assetra.Core.Models.Sync;
using Assetra.Infrastructure.Sync;
using Microsoft.Data.Sqlite;

namespace Assetra.Infrastructure.Persistence;

public sealed class InsurancePolicySqliteRepository : IInsurancePolicyRepository, IInsurancePolicySyncStore
{
    private readonly string _connectionString;
    private readonly Func<string> _deviceIdProvider;
    private readonly TimeProvider _time;

    public InsurancePolicySqliteRepository(string dbPath, string deviceId = "local", TimeProvider? time = null)
        : this(dbPath, () => deviceId, time)
    {
    }

    public InsurancePolicySqliteRepository(string dbPath, Func<string> deviceIdProvider, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(deviceIdProvider);
        _connectionString = $"Data Source={dbPath}";
        _deviceIdProvider = deviceIdProvider;
        _time = time ?? TimeProvider.System;
        InsuranceSchemaMigrator.EnsureInitialized(_connectionString);
    }

    // ─── IInsurancePolicyRepository ─────────────────────────────────────────

    public async Task<IReadOnlyList<InsurancePolicy>> GetAllAsync(CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, name, policy_number, type, insurer, start_date, maturity_date,
                   face_value, current_cash_value, annual_premium, currency, status, notes,
                   ev_version, ev_modified_at, ev_device_id
            FROM insurance_policy
            WHERE is_deleted = 0
            ORDER BY rowid;
            """;
        var results = new List<InsurancePolicy>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            results.Add(MapRow(reader));
        return results;
    }

    public async Task<InsurancePolicy?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, name, policy_number, type, insurer, start_date, maturity_date,
                   face_value, current_cash_value, annual_premium, currency, status, notes,
                   ev_version, ev_modified_at, ev_device_id
            FROM insurance_policy WHERE id = $id AND is_deleted = 0;
            """;
        cmd.Parameters.AddWithValue("$id", id.ToString());
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false) ? MapRow(reader) : null;
    }

    public async Task AddAsync(InsurancePolicy policy, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO insurance_policy
                (id, name, policy_number, type, insurer, start_date, maturity_date,
                 face_value, current_cash_value, annual_premium, currency, status, notes,
                 ev_version, ev_modified_at, ev_device_id,
                 is_deleted, is_pending_push, created_at, updated_at)
            VALUES
                ($id, $name, $policy_number, $type, $insurer, $start_date, $maturity_date,
                 $face_value, $current_cash_value, $annual_premium, $currency, $status, $notes,
                 $ev_version, $ev_modified_at, $ev_device_id,
                 0, 1, $now, $now);
            """;
        BindParams(cmd, policy);
        cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task UpdateAsync(InsurancePolicy policy, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE insurance_policy SET
                name               = $name,
                policy_number      = $policy_number,
                type               = $type,
                insurer            = $insurer,
                start_date         = $start_date,
                maturity_date      = $maturity_date,
                face_value         = $face_value,
                current_cash_value = $current_cash_value,
                annual_premium     = $annual_premium,
                currency           = $currency,
                status             = $status,
                notes              = $notes,
                ev_version         = $ev_version,
                ev_modified_at     = $ev_modified_at,
                ev_device_id       = $ev_device_id,
                is_pending_push    = 1,
                updated_at         = $now
            WHERE id = $id;
            """;
        BindParams(cmd, policy);
        cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task RemoveAsync(Guid id, CancellationToken ct = default)
    {
        // Soft delete — keeps the tombstone for sync
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE insurance_policy SET
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

    // ─── IInsurancePolicySyncStore ──────────────────────────────────────────

    public async Task<IReadOnlyList<SyncEnvelope>> GetPendingPushAsync(CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, name, policy_number, type, insurer, start_date, maturity_date,
                   face_value, current_cash_value, annual_premium, currency, status, notes,
                   ev_version, ev_modified_at, ev_device_id, is_deleted
            FROM insurance_policy WHERE is_pending_push = 1;
            """;
        var results = new List<SyncEnvelope>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var version = new EntityVersion(
                reader.GetInt64(13),
                reader.IsDBNull(14) || reader.GetString(14).Length == 0
                    ? DateTimeOffset.UtcNow
                    : DateTimeOffset.Parse(reader.GetString(14)),
                reader.IsDBNull(15) ? string.Empty : reader.GetString(15));
            var isDeleted = reader.GetInt64(16) != 0;
            var policy = MapRowFull(reader, version);
            results.Add(InsurancePolicySyncMapper.ToEnvelope(policy, isDeleted));
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
        cmd.CommandText = "UPDATE insurance_policy SET is_pending_push = 0 WHERE id = $id;";
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
            if (env.EntityType != InsurancePolicySyncMapper.EntityType) continue;

            await using (var probe = conn.CreateCommand())
            {
                probe.Transaction = tx;
                probe.CommandText = "SELECT ev_version FROM insurance_policy WHERE id = $id;";
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
                    INSERT INTO insurance_policy
                        (id, name, policy_number, type, insurer, start_date,
                         face_value, current_cash_value, annual_premium, currency, status,
                         ev_version, ev_modified_at, ev_device_id,
                         is_deleted, is_pending_push, created_at, updated_at)
                    VALUES
                        ($id, '', '', 'Other', '', '2000-01-01',
                         0, 0, 0, 'TWD', 'Active',
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
                var policy = InsurancePolicySyncMapper.FromPayload(env, env.Version);
                await using var upsert = conn.CreateCommand();
                upsert.Transaction = tx;
                upsert.CommandText = """
                    INSERT INTO insurance_policy
                        (id, name, policy_number, type, insurer, start_date, maturity_date,
                         face_value, current_cash_value, annual_premium, currency, status, notes,
                         ev_version, ev_modified_at, ev_device_id,
                         is_deleted, is_pending_push, created_at, updated_at)
                    VALUES
                        ($id, $name, $policy_number, $type, $insurer, $start_date, $maturity_date,
                         $face_value, $current_cash_value, $annual_premium, $currency, $status, $notes,
                         $ev_version, $ev_modified_at, $ev_device_id,
                         0, 0, $now, $now)
                    ON CONFLICT(id) DO UPDATE SET
                        name               = excluded.name,
                        policy_number      = excluded.policy_number,
                        type               = excluded.type,
                        insurer            = excluded.insurer,
                        start_date         = excluded.start_date,
                        maturity_date      = excluded.maturity_date,
                        face_value         = excluded.face_value,
                        current_cash_value = excluded.current_cash_value,
                        annual_premium     = excluded.annual_premium,
                        currency           = excluded.currency,
                        status             = excluded.status,
                        notes              = excluded.notes,
                        ev_version         = excluded.ev_version,
                        ev_modified_at     = excluded.ev_modified_at,
                        ev_device_id       = excluded.ev_device_id,
                        is_deleted         = 0,
                        is_pending_push    = 0,
                        updated_at         = excluded.updated_at;
                    """;
                BindParams(upsert, policy);
                upsert.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
                await upsert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
        }
        await tx.CommitAsync(ct).ConfigureAwait(false);
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    private static InsurancePolicy MapRow(SqliteDataReader r)
    {
        var version = new EntityVersion(
            r.GetInt64(13),
            r.IsDBNull(14) || r.GetString(14).Length == 0
                ? default
                : DateTimeOffset.Parse(r.GetString(14)),
            r.IsDBNull(15) ? string.Empty : r.GetString(15));
        return MapRowFull(r, version);
    }

    private static InsurancePolicy MapRowFull(SqliteDataReader r, EntityVersion version) =>
        new(
            Id: Guid.Parse(r.GetString(0)),
            Name: r.GetString(1),
            PolicyNumber: r.GetString(2),
            Type: Enum.Parse<InsuranceType>(r.GetString(3)),
            Insurer: r.GetString(4),
            StartDate: DateOnly.Parse(r.GetString(5)),
            MaturityDate: r.IsDBNull(6) ? null : DateOnly.Parse(r.GetString(6)),
            FaceValue: (decimal)r.GetDouble(7),
            CurrentCashValue: (decimal)r.GetDouble(8),
            AnnualPremium: (decimal)r.GetDouble(9),
            Currency: r.GetString(10),
            Status: Enum.Parse<InsurancePolicyStatus>(r.GetString(11)),
            Notes: r.IsDBNull(12) ? null : r.GetString(12),
            Version: version);

    private string CurrentDeviceId()
    {
        var deviceId = _deviceIdProvider();
        return string.IsNullOrWhiteSpace(deviceId) ? "local" : deviceId;
    }

    private static void BindParams(SqliteCommand cmd, InsurancePolicy p)
    {
        cmd.Parameters.AddWithValue("$id", p.Id.ToString());
        cmd.Parameters.AddWithValue("$name", p.Name);
        cmd.Parameters.AddWithValue("$policy_number", p.PolicyNumber);
        cmd.Parameters.AddWithValue("$type", p.Type.ToString());
        cmd.Parameters.AddWithValue("$insurer", p.Insurer);
        cmd.Parameters.AddWithValue("$start_date", p.StartDate.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$maturity_date",
            p.MaturityDate?.ToString("yyyy-MM-dd") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$face_value", (double)p.FaceValue);
        cmd.Parameters.AddWithValue("$current_cash_value", (double)p.CurrentCashValue);
        cmd.Parameters.AddWithValue("$annual_premium", (double)p.AnnualPremium);
        cmd.Parameters.AddWithValue("$currency", p.Currency);
        cmd.Parameters.AddWithValue("$status", p.Status.ToString());
        cmd.Parameters.AddWithValue("$notes", p.Notes ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$ev_version", p.Version.Version);
        cmd.Parameters.AddWithValue("$ev_modified_at", p.Version.LastModifiedAt == default
            ? string.Empty : p.Version.LastModifiedAt.ToString("o"));
        cmd.Parameters.AddWithValue("$ev_device_id", p.Version.LastModifiedByDevice);
    }
}
