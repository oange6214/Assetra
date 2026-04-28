using Assetra.Core.Interfaces.MultiAsset;
using Assetra.Core.Interfaces.Sync;
using Assetra.Core.Models.MultiAsset;
using Assetra.Core.Models.Sync;
using Assetra.Infrastructure.Sync;
using Microsoft.Data.Sqlite;

namespace Assetra.Infrastructure.Persistence;

public sealed class PhysicalAssetSqliteRepository : IPhysicalAssetRepository, IPhysicalAssetSyncStore
{
    private readonly string _connectionString;

    public PhysicalAssetSqliteRepository(string dbPath)
    {
        _connectionString = $"Data Source={dbPath}";
        PhysicalAssetSchemaMigrator.EnsureInitialized(_connectionString);
    }

    public async Task<IReadOnlyList<PhysicalAsset>> GetAllAsync(CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, name, category, description, acquisition_cost, acquisition_date,
                   current_value, valuation_method, currency, status, notes,
                   ev_version, ev_modified_at, ev_device_id
            FROM physical_asset
            WHERE is_deleted = 0
            ORDER BY rowid;
            """;
        var results = new List<PhysicalAsset>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            results.Add(MapRow(reader));
        return results;
    }

    public async Task<PhysicalAsset?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, name, category, description, acquisition_cost, acquisition_date,
                   current_value, valuation_method, currency, status, notes,
                   ev_version, ev_modified_at, ev_device_id
            FROM physical_asset WHERE id = $id AND is_deleted = 0;
            """;
        cmd.Parameters.AddWithValue("$id", id.ToString());
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false) ? MapRow(reader) : null;
    }

    public async Task AddAsync(PhysicalAsset entity, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO physical_asset
                (id, name, category, description, acquisition_cost, acquisition_date,
                 current_value, valuation_method, currency, status, notes,
                 ev_version, ev_modified_at, ev_device_id,
                 is_deleted, is_pending_push, created_at, updated_at)
            VALUES
                ($id, $name, $category, $description, $acquisition_cost, $acquisition_date,
                 $current_value, $valuation_method, $currency, $status, $notes,
                 $ev_version, $ev_modified_at, $ev_device_id,
                 0, 1, $now, $now);
            """;
        BindParams(cmd, entity);
        cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task UpdateAsync(PhysicalAsset entity, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE physical_asset SET
                name             = $name,
                category         = $category,
                description      = $description,
                acquisition_cost = $acquisition_cost,
                acquisition_date = $acquisition_date,
                current_value    = $current_value,
                valuation_method = $valuation_method,
                currency         = $currency,
                status           = $status,
                notes            = $notes,
                ev_version       = $ev_version,
                ev_modified_at   = $ev_modified_at,
                ev_device_id     = $ev_device_id,
                is_pending_push  = 1,
                updated_at       = $now
            WHERE id = $id;
            """;
        BindParams(cmd, entity);
        cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task RemoveAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE physical_asset SET
                is_deleted      = 1,
                is_pending_push = 1,
                ev_version      = ev_version + 1,
                updated_at      = $now
            WHERE id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", id.ToString());
        cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SyncEnvelope>> GetPendingPushAsync(CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, name, category, description, acquisition_cost, acquisition_date,
                   current_value, valuation_method, currency, status, notes,
                   ev_version, ev_modified_at, ev_device_id, is_deleted
            FROM physical_asset WHERE is_pending_push = 1;
            """;
        var results = new List<SyncEnvelope>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var version = new EntityVersion(
                reader.GetInt64(11),
                reader.IsDBNull(12) || reader.GetString(12).Length == 0
                    ? DateTimeOffset.UtcNow
                    : DateTimeOffset.Parse(reader.GetString(12)),
                reader.IsDBNull(13) ? string.Empty : reader.GetString(13));
            var isDeleted = reader.GetInt64(14) != 0;
            var entity = MapRowFull(reader, version);
            results.Add(PhysicalAssetSyncMapper.ToEnvelope(entity, isDeleted));
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
        cmd.CommandText = "UPDATE physical_asset SET is_pending_push = 0 WHERE id = $id;";
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
            if (env.EntityType != PhysicalAssetSyncMapper.EntityType) continue;

            await using (var probe = conn.CreateCommand())
            {
                probe.Transaction = tx;
                probe.CommandText = "SELECT ev_version FROM physical_asset WHERE id = $id;";
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
                    INSERT INTO physical_asset
                        (id, name, category, description, acquisition_cost, acquisition_date,
                         current_value, valuation_method, currency, status,
                         ev_version, ev_modified_at, ev_device_id,
                         is_deleted, is_pending_push, created_at, updated_at)
                    VALUES
                        ($id, '', 'Other', '', 0, '2000-01-01', 0, '', 'TWD', 'Sold',
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
                var entity = PhysicalAssetSyncMapper.FromPayload(env, env.Version);
                await using var upsert = conn.CreateCommand();
                upsert.Transaction = tx;
                upsert.CommandText = """
                    INSERT INTO physical_asset
                        (id, name, category, description, acquisition_cost, acquisition_date,
                         current_value, valuation_method, currency, status, notes,
                         ev_version, ev_modified_at, ev_device_id,
                         is_deleted, is_pending_push, created_at, updated_at)
                    VALUES
                        ($id, $name, $category, $description, $acquisition_cost, $acquisition_date,
                         $current_value, $valuation_method, $currency, $status, $notes,
                         $ev_version, $ev_modified_at, $ev_device_id,
                         0, 0, $now, $now)
                    ON CONFLICT(id) DO UPDATE SET
                        name             = excluded.name,
                        category         = excluded.category,
                        description      = excluded.description,
                        acquisition_cost = excluded.acquisition_cost,
                        acquisition_date = excluded.acquisition_date,
                        current_value    = excluded.current_value,
                        valuation_method = excluded.valuation_method,
                        currency         = excluded.currency,
                        status           = excluded.status,
                        notes            = excluded.notes,
                        ev_version       = excluded.ev_version,
                        ev_modified_at   = excluded.ev_modified_at,
                        ev_device_id     = excluded.ev_device_id,
                        is_deleted       = 0,
                        is_pending_push  = 0,
                        updated_at       = excluded.updated_at;
                    """;
                BindParams(upsert, entity);
                upsert.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
                await upsert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
        }
        await tx.CommitAsync(ct).ConfigureAwait(false);
    }

    private static PhysicalAsset MapRow(SqliteDataReader r)
    {
        var version = new EntityVersion(
            r.GetInt64(11),
            r.IsDBNull(12) || r.GetString(12).Length == 0
                ? default
                : DateTimeOffset.Parse(r.GetString(12)),
            r.IsDBNull(13) ? string.Empty : r.GetString(13));
        return MapRowFull(r, version);
    }

    private static PhysicalAsset MapRowFull(SqliteDataReader r, EntityVersion version) =>
        new(
            Id: Guid.Parse(r.GetString(0)),
            Name: r.GetString(1),
            Category: Enum.Parse<PhysicalAssetCategory>(r.GetString(2)),
            Description: r.GetString(3),
            AcquisitionCost: (decimal)r.GetDouble(4),
            AcquisitionDate: DateOnly.Parse(r.GetString(5)),
            CurrentValue: (decimal)r.GetDouble(6),
            ValuationMethod: r.GetString(7),
            Currency: r.GetString(8),
            Status: Enum.Parse<PhysicalAssetStatus>(r.GetString(9)),
            Notes: r.IsDBNull(10) ? null : r.GetString(10),
            Version: version);

    private static void BindParams(SqliteCommand cmd, PhysicalAsset e)
    {
        cmd.Parameters.AddWithValue("$id", e.Id.ToString());
        cmd.Parameters.AddWithValue("$name", e.Name);
        cmd.Parameters.AddWithValue("$category", e.Category.ToString());
        cmd.Parameters.AddWithValue("$description", e.Description);
        cmd.Parameters.AddWithValue("$acquisition_cost", (double)e.AcquisitionCost);
        cmd.Parameters.AddWithValue("$acquisition_date", e.AcquisitionDate.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$current_value", (double)e.CurrentValue);
        cmd.Parameters.AddWithValue("$valuation_method", e.ValuationMethod);
        cmd.Parameters.AddWithValue("$currency", e.Currency);
        cmd.Parameters.AddWithValue("$status", e.Status.ToString());
        cmd.Parameters.AddWithValue("$notes", e.Notes ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$ev_version", e.Version.Version);
        cmd.Parameters.AddWithValue("$ev_modified_at", e.Version.LastModifiedAt == default
            ? string.Empty : e.Version.LastModifiedAt.ToString("o"));
        cmd.Parameters.AddWithValue("$ev_device_id", e.Version.LastModifiedByDevice);
    }
}
