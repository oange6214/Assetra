using Assetra.Core.Interfaces.MultiAsset;
using Assetra.Core.Interfaces.Sync;
using Assetra.Core.Models.MultiAsset;
using Assetra.Core.Models.Sync;
using Assetra.Infrastructure.Sync;
using Microsoft.Data.Sqlite;

namespace Assetra.Infrastructure.Persistence;

public sealed class RealEstateSqliteRepository : IRealEstateRepository, IRealEstateSyncStore
{
    private readonly string _connectionString;
    private readonly Func<string> _deviceIdProvider;
    private readonly TimeProvider _time;

    public RealEstateSqliteRepository(
        string dbPath,
        Func<string>? deviceIdProvider = null,
        TimeProvider? time = null)
    {
        _connectionString = $"Data Source={dbPath}";
        _deviceIdProvider = deviceIdProvider ?? (() => string.Empty);
        _time = time ?? TimeProvider.System;
        RealEstateSchemaMigrator.EnsureInitialized(_connectionString);
    }

    // ─── IRealEstateRepository ──────────────────────────────────────────────

    public async Task<IReadOnlyList<RealEstate>> GetAllAsync(CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, name, address, purchase_price, purchase_date, current_value,
                   mortgage_balance, currency, is_rental, status, notes,
                   ev_version, ev_modified_at, ev_device_id
            FROM real_estate
            WHERE is_deleted = 0
            ORDER BY rowid;
            """;
        var results = new List<RealEstate>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            results.Add(MapRow(reader));
        return results;
    }

    public async Task<RealEstate?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, name, address, purchase_price, purchase_date, current_value,
                   mortgage_balance, currency, is_rental, status, notes,
                   ev_version, ev_modified_at, ev_device_id
            FROM real_estate WHERE id = $id AND is_deleted = 0;
            """;
        cmd.Parameters.AddWithValue("$id", id.ToString());
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false) ? MapRow(reader) : null;
    }

    public async Task AddAsync(RealEstate entity, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        var now = _time.GetUtcNow();
        var stamped = entity with { Version = StampVersion(entity.Version, now) };
        cmd.CommandText = """
            INSERT INTO real_estate
                (id, name, address, purchase_price, purchase_date, current_value,
                 mortgage_balance, currency, is_rental, status, notes,
                 ev_version, ev_modified_at, ev_device_id,
                 is_deleted, is_pending_push, created_at, updated_at)
            VALUES
                ($id, $name, $address, $purchase_price, $purchase_date, $current_value,
                 $mortgage_balance, $currency, $is_rental, $status, $notes,
                 $ev_version, $ev_modified_at, $ev_device_id,
                 0, 1, $now, $now);
            """;
        BindParams(cmd, stamped);
        cmd.Parameters.AddWithValue("$now", now.ToString("o"));
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task UpdateAsync(RealEstate entity, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        var now = _time.GetUtcNow();
        var currentVersion = await GetVersionAsync(conn, entity.Id, ct).ConfigureAwait(false) ?? entity.Version;
        var stamped = entity with { Version = StampVersion(currentVersion, now) };
        cmd.CommandText = """
            UPDATE real_estate SET
                name             = $name,
                address          = $address,
                purchase_price   = $purchase_price,
                purchase_date    = $purchase_date,
                current_value    = $current_value,
                mortgage_balance = $mortgage_balance,
                currency         = $currency,
                is_rental        = $is_rental,
                status           = $status,
                notes            = $notes,
                ev_version       = $ev_version,
                ev_modified_at   = $ev_modified_at,
                ev_device_id     = $ev_device_id,
                is_pending_push  = 1,
                updated_at       = $now
            WHERE id = $id;
            """;
        BindParams(cmd, stamped);
        cmd.Parameters.AddWithValue("$now", now.ToString("o"));
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task RemoveAsync(Guid id, CancellationToken ct = default)
    {
        // Soft delete — keeps the tombstone for sync
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        var now = _time.GetUtcNow();
        var deviceId = CurrentDeviceId();
        cmd.CommandText = """
            UPDATE real_estate SET
                is_deleted      = 1,
                is_pending_push = 1,
                ev_version      = ev_version + 1,
                ev_modified_at  = $now,
                ev_device_id    = $device_id,
                updated_at      = $now
            WHERE id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", id.ToString());
        cmd.Parameters.AddWithValue("$device_id", deviceId);
        cmd.Parameters.AddWithValue("$now", now.ToString("o"));
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    // ─── IRealEstateSyncStore ───────────────────────────────────────────────

    public async Task<IReadOnlyList<SyncEnvelope>> GetPendingPushAsync(CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, name, address, purchase_price, purchase_date, current_value,
                   mortgage_balance, currency, is_rental, status, notes,
                   ev_version, ev_modified_at, ev_device_id, is_deleted
            FROM real_estate WHERE is_pending_push = 1;
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
            results.Add(RealEstateSyncMapper.ToEnvelope(entity, isDeleted));
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
        cmd.CommandText = "UPDATE real_estate SET is_pending_push = 0 WHERE id = $id;";
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
            if (env.EntityType != RealEstateSyncMapper.EntityType) continue;

            // Skip if local version is already at or ahead of remote
            await using (var probe = conn.CreateCommand())
            {
                probe.Transaction = tx;
                probe.CommandText = "SELECT ev_version FROM real_estate WHERE id = $id;";
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
                    INSERT INTO real_estate
                        (id, name, address, purchase_price, purchase_date, current_value,
                         mortgage_balance, currency, is_rental, status,
                         ev_version, ev_modified_at, ev_device_id,
                         is_deleted, is_pending_push, created_at, updated_at)
                    VALUES
                        ($id, '', '', 0, '2000-01-01', 0, 0, 'TWD', 0, 'Active',
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
                var entity = RealEstateSyncMapper.FromPayload(env, env.Version);
                await using var upsert = conn.CreateCommand();
                upsert.Transaction = tx;
                upsert.CommandText = """
                    INSERT INTO real_estate
                        (id, name, address, purchase_price, purchase_date, current_value,
                         mortgage_balance, currency, is_rental, status, notes,
                         ev_version, ev_modified_at, ev_device_id,
                         is_deleted, is_pending_push, created_at, updated_at)
                    VALUES
                        ($id, $name, $address, $purchase_price, $purchase_date, $current_value,
                         $mortgage_balance, $currency, $is_rental, $status, $notes,
                         $ev_version, $ev_modified_at, $ev_device_id,
                         0, 0, $now, $now)
                    ON CONFLICT(id) DO UPDATE SET
                        name             = excluded.name,
                        address          = excluded.address,
                        purchase_price   = excluded.purchase_price,
                        purchase_date    = excluded.purchase_date,
                        current_value    = excluded.current_value,
                        mortgage_balance = excluded.mortgage_balance,
                        currency         = excluded.currency,
                        is_rental        = excluded.is_rental,
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

    // ─── Helpers ────────────────────────────────────────────────────────────

    private string CurrentDeviceId() => _deviceIdProvider() ?? string.Empty;

    private EntityVersion StampVersion(EntityVersion version, DateTimeOffset now)
    {
        var deviceId = CurrentDeviceId();
        return version.Version <= 0
            ? EntityVersion.Initial(deviceId, now)
            : version.Bump(deviceId, now);
    }

    private static async Task<EntityVersion?> GetVersionAsync(
        SqliteConnection conn,
        Guid id,
        CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT ev_version, ev_modified_at, ev_device_id
            FROM real_estate
            WHERE id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", id.ToString());
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            return null;

        return new EntityVersion(
            reader.GetInt64(0),
            reader.IsDBNull(1) || reader.GetString(1).Length == 0
                ? default
                : DateTimeOffset.Parse(reader.GetString(1)),
            reader.IsDBNull(2) ? string.Empty : reader.GetString(2));
    }

    private static RealEstate MapRow(SqliteDataReader r)
    {
        var version = new EntityVersion(
            r.GetInt64(11),
            r.IsDBNull(12) || r.GetString(12).Length == 0
                ? default
                : DateTimeOffset.Parse(r.GetString(12)),
            r.IsDBNull(13) ? string.Empty : r.GetString(13));
        return MapRowFull(r, version);
    }

    private static RealEstate MapRowFull(SqliteDataReader r, EntityVersion version) =>
        new(
            Id: Guid.Parse(r.GetString(0)),
            Name: r.GetString(1),
            Address: r.GetString(2),
            PurchasePrice: (decimal)r.GetDouble(3),
            PurchaseDate: DateOnly.Parse(r.GetString(4)),
            CurrentValue: (decimal)r.GetDouble(5),
            MortgageBalance: (decimal)r.GetDouble(6),
            Currency: r.GetString(7),
            IsRental: r.GetInt64(8) != 0,
            Status: Enum.Parse<RealEstateStatus>(r.GetString(9)),
            Notes: r.IsDBNull(10) ? null : r.GetString(10),
            Version: version);

    private static void BindParams(SqliteCommand cmd, RealEstate e)
    {
        cmd.Parameters.AddWithValue("$id", e.Id.ToString());
        cmd.Parameters.AddWithValue("$name", e.Name);
        cmd.Parameters.AddWithValue("$address", e.Address);
        cmd.Parameters.AddWithValue("$purchase_price", (double)e.PurchasePrice);
        cmd.Parameters.AddWithValue("$purchase_date", e.PurchaseDate.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$current_value", (double)e.CurrentValue);
        cmd.Parameters.AddWithValue("$mortgage_balance", (double)e.MortgageBalance);
        cmd.Parameters.AddWithValue("$currency", e.Currency);
        cmd.Parameters.AddWithValue("$is_rental", e.IsRental ? 1 : 0);
        cmd.Parameters.AddWithValue("$status", e.Status.ToString());
        cmd.Parameters.AddWithValue("$notes", e.Notes ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$ev_version", e.Version.Version);
        cmd.Parameters.AddWithValue("$ev_modified_at", e.Version.LastModifiedAt == default
            ? string.Empty : e.Version.LastModifiedAt.ToString("o"));
        cmd.Parameters.AddWithValue("$ev_device_id", e.Version.LastModifiedByDevice);
    }
}
