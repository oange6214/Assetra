using System.Globalization;
using Assetra.Core.Interfaces;
using Assetra.Core.Interfaces.Sync;
using Assetra.Core.Models;
using Assetra.Core.Models.Sync;
using Assetra.Infrastructure.Sync;
using Microsoft.Data.Sqlite;

namespace Assetra.Infrastructure.Persistence;

/// <summary>
/// RecurringTransaction 的 SQLite 實作（v0.20.11）。
/// 同時實作 <see cref="IRecurringTransactionRepository"/> 與
/// <see cref="IRecurringTransactionSyncStore"/>。
/// 寫入時 stamp version/last_modified_at/last_modified_by_device/is_pending_push；
/// 刪除為 soft delete（tombstone）；查詢預設過濾 is_deleted = 0。
/// PendingRecurringEntry 不同步（per-device materialized queue）。
/// </summary>
public sealed class RecurringTransactionSqliteRepository
    : IRecurringTransactionRepository, IRecurringTransactionSyncStore
{
    private readonly string _connectionString;
    private readonly Func<string> _deviceIdProvider;
    private readonly TimeProvider _time;

    public RecurringTransactionSqliteRepository(
        string dbPath,
        string deviceId = "local",
        TimeProvider? time = null)
        : this(dbPath, () => deviceId, time)
    {
    }

    public RecurringTransactionSqliteRepository(
        string dbPath,
        Func<string> deviceIdProvider,
        TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(deviceIdProvider);
        _connectionString = $"Data Source={dbPath}";
        _deviceIdProvider = deviceIdProvider;
        _time = time ?? TimeProvider.System;
        RecurringSchemaMigrator.EnsureInitialized(_connectionString);
    }

    private const string SelectClause =
        "id, name, trade_type, amount, cash_account_id, category_id, frequency, interval_value, " +
        "start_date, end_date, generation_mode, last_generated_at, next_due_at, note, is_enabled";

    private const string SyncSelectClause =
        SelectClause + ", version, last_modified_at, last_modified_by_device, is_deleted";

    private static RecurringTransaction Map(SqliteDataReader r) => new(
        Id: Guid.Parse(r.GetString(0)),
        Name: r.GetString(1),
        TradeType: (TradeType)r.GetInt32(2),
        Amount: decimal.Parse(r.GetString(3), CultureInfo.InvariantCulture),
        CashAccountId: r.IsDBNull(4) ? null : Guid.Parse(r.GetString(4)),
        CategoryId: r.IsDBNull(5) ? null : Guid.Parse(r.GetString(5)),
        Frequency: (RecurrenceFrequency)r.GetInt32(6),
        Interval: r.GetInt32(7),
        StartDate: DateTime.Parse(r.GetString(8), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
        EndDate: r.IsDBNull(9) ? null : DateTime.Parse(r.GetString(9), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
        GenerationMode: (AutoGenerationMode)r.GetInt32(10),
        LastGeneratedAt: r.IsDBNull(11) ? null : DateTime.Parse(r.GetString(11), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
        NextDueAt: r.IsDBNull(12) ? null : DateTime.Parse(r.GetString(12), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
        Note: r.IsDBNull(13) ? null : r.GetString(13),
        IsEnabled: r.GetInt32(14) != 0);

    public async Task<IReadOnlyList<RecurringTransaction>> GetAllAsync(CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {SelectClause} FROM recurring_transaction WHERE is_deleted = 0 ORDER BY name;";
        var results = new List<RecurringTransaction>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            results.Add(Map(reader));
        return results;
    }

    public async Task<IReadOnlyList<RecurringTransaction>> GetActiveAsync(CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {SelectClause} FROM recurring_transaction WHERE is_deleted = 0 AND is_enabled = 1 ORDER BY next_due_at;";
        var results = new List<RecurringTransaction>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            results.Add(Map(reader));
        return results;
    }

    public async Task<RecurringTransaction?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {SelectClause} FROM recurring_transaction WHERE id = $id AND is_deleted = 0;";
        cmd.Parameters.AddWithValue("$id", id.ToString());
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false) ? Map(reader) : null;
    }

    /// <summary>
    /// SQL <c>COUNT(*)</c> override — see ITradeRepository.CountByCategoryAsync
    /// for rationale. Used by Categories.DeleteAsync pre-check to avoid
    /// loading all rows when only a count is needed.
    /// </summary>
    public async Task<int> CountByCategoryAsync(Guid categoryId, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM recurring_transaction WHERE category_id = $cat AND is_deleted = 0;";
        cmd.Parameters.AddWithValue("$cat", categoryId.ToString());
        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture);
    }

    public async Task AddAsync(RecurringTransaction recurring, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(recurring);
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR IGNORE INTO recurring_transaction
                (id, name, trade_type, amount, cash_account_id, category_id, frequency, interval_value,
                 start_date, end_date, generation_mode, last_generated_at, next_due_at, note, is_enabled,
                 created_at, updated_at,
                 version, last_modified_at, last_modified_by_device, is_deleted, is_pending_push)
            VALUES
                ($id, $name, $type, $amt, $cash, $cat, $freq, $interval,
                 $start, $end, $mode, $last, $next, $note, $enabled, $now, $now,
                 1, $now, $device, 0, 1);
            """;
        Bind(cmd, recurring);
        StampSync(cmd);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task UpdateAsync(RecurringTransaction recurring, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(recurring);
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE recurring_transaction SET
                name              = $name,
                trade_type        = $type,
                amount            = $amt,
                cash_account_id   = $cash,
                category_id       = $cat,
                frequency         = $freq,
                interval_value    = $interval,
                start_date        = $start,
                end_date          = $end,
                generation_mode   = $mode,
                last_generated_at = $last,
                next_due_at       = $next,
                note              = $note,
                is_enabled        = $enabled,
                updated_at        = $now,
                version           = version + 1,
                last_modified_at  = $now,
                last_modified_by_device = $device,
                is_pending_push   = 1
            WHERE id = $id;
            """;
        Bind(cmd, recurring);
        StampSync(cmd);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task RemoveAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE recurring_transaction SET
                is_deleted              = 1,
                version                 = version + 1,
                last_modified_at        = $now,
                last_modified_by_device = $device,
                is_pending_push         = 1,
                updated_at              = $now
            WHERE id = $id AND is_deleted = 0;
            """;
        cmd.Parameters.AddWithValue("$id", id.ToString());
        StampSync(cmd);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SyncEnvelope>> GetPendingPushAsync(CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT {SyncSelectClause}
            FROM recurring_transaction
            WHERE is_pending_push = 1
            ORDER BY last_modified_at;
            """;
        var results = new List<SyncEnvelope>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var version = ReadVersion(reader);
            var isDeleted = reader.GetInt32(18) != 0;
            if (isDeleted)
            {
                var id = Guid.Parse(reader.GetString(0));
                results.Add(new SyncEnvelope(id, RecurringTransactionSyncMapper.EntityType, string.Empty, version, Deleted: true));
            }
            else
            {
                var rt = Map(reader);
                results.Add(RecurringTransactionSyncMapper.ToEnvelope(rt, version, isDeleted: false));
            }
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
        await using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE recurring_transaction SET is_pending_push = 0 WHERE id = $id;";
            var p = cmd.Parameters.Add("$id", SqliteType.Text);
            foreach (var id in ids)
            {
                p.Value = id.ToString();
                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
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
            if (env.EntityType != RecurringTransactionSyncMapper.EntityType) continue;

            long localVersion = -1;
            await using (var probe = conn.CreateCommand())
            {
                probe.Transaction = tx;
                probe.CommandText = "SELECT version FROM recurring_transaction WHERE id = $id;";
                probe.Parameters.AddWithValue("$id", env.EntityId.ToString());
                var existing = await probe.ExecuteScalarAsync(ct).ConfigureAwait(false);
                if (existing is not null and not DBNull) localVersion = Convert.ToInt64(existing, CultureInfo.InvariantCulture);
            }

            if (localVersion >= env.Version.Version) continue;

            if (env.Deleted)
            {
                await using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = """
                    INSERT INTO recurring_transaction
                        (id, name, trade_type, amount, cash_account_id, category_id, frequency, interval_value,
                         start_date, end_date, generation_mode, last_generated_at, next_due_at, note, is_enabled,
                         created_at, updated_at,
                         version, last_modified_at, last_modified_by_device, is_deleted, is_pending_push)
                    VALUES
                        ($id, '', 0, '0', NULL, NULL, 0, 1,
                         $now, NULL, 0, NULL, NULL, NULL, 0,
                         $now, $now,
                         $ver, $modAt, $modBy, 1, 0)
                    ON CONFLICT(id) DO UPDATE SET
                        is_deleted              = 1,
                        version                 = excluded.version,
                        last_modified_at        = excluded.last_modified_at,
                        last_modified_by_device = excluded.last_modified_by_device,
                        is_pending_push         = 0,
                        updated_at              = excluded.updated_at;
                    """;
                cmd.Parameters.AddWithValue("$id", env.EntityId.ToString());
                cmd.Parameters.AddWithValue("$ver", env.Version.Version);
                cmd.Parameters.AddWithValue("$modAt", env.Version.LastModifiedAt.ToString("o", CultureInfo.InvariantCulture));
                cmd.Parameters.AddWithValue("$modBy", env.Version.LastModifiedByDevice);
                StampSync(cmd);
                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
            else
            {
                var rt = RecurringTransactionSyncMapper.FromPayload(env);
                await using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = """
                    INSERT INTO recurring_transaction
                        (id, name, trade_type, amount, cash_account_id, category_id, frequency, interval_value,
                         start_date, end_date, generation_mode, last_generated_at, next_due_at, note, is_enabled,
                         created_at, updated_at,
                         version, last_modified_at, last_modified_by_device, is_deleted, is_pending_push)
                    VALUES
                        ($id, $name, $type, $amt, $cash, $cat, $freq, $interval,
                         $start, $end, $mode, $last, $next, $note, $enabled,
                         $now, $now,
                         $ver, $modAt, $modBy, 0, 0)
                    ON CONFLICT(id) DO UPDATE SET
                        name              = excluded.name,
                        trade_type        = excluded.trade_type,
                        amount            = excluded.amount,
                        cash_account_id   = excluded.cash_account_id,
                        category_id       = excluded.category_id,
                        frequency         = excluded.frequency,
                        interval_value    = excluded.interval_value,
                        start_date        = excluded.start_date,
                        end_date          = excluded.end_date,
                        generation_mode   = excluded.generation_mode,
                        last_generated_at = excluded.last_generated_at,
                        next_due_at       = excluded.next_due_at,
                        note              = excluded.note,
                        is_enabled        = excluded.is_enabled,
                        updated_at        = excluded.updated_at,
                        version           = excluded.version,
                        last_modified_at  = excluded.last_modified_at,
                        last_modified_by_device = excluded.last_modified_by_device,
                        is_deleted        = 0,
                        is_pending_push   = 0;
                    """;
                Bind(cmd, rt);
                cmd.Parameters.AddWithValue("$ver", env.Version.Version);
                cmd.Parameters.AddWithValue("$modAt", env.Version.LastModifiedAt.ToString("o", CultureInfo.InvariantCulture));
                cmd.Parameters.AddWithValue("$modBy", env.Version.LastModifiedByDevice);
                StampSync(cmd);
                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
        }

        await tx.CommitAsync(ct).ConfigureAwait(false);
    }

    private static EntityVersion ReadVersion(SqliteDataReader r)
    {
        var version = r.GetInt64(15);
        var modAt = DateTimeOffset.Parse(r.GetString(16), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        var modBy = r.GetString(17);
        return new EntityVersion(version, modAt, modBy);
    }

    private string NowIso() => _time.GetUtcNow().UtcDateTime.ToString("o", CultureInfo.InvariantCulture);

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

    private static void Bind(SqliteCommand cmd, RecurringTransaction r)
    {
        cmd.Parameters.AddWithValue("$id", r.Id.ToString());
        cmd.Parameters.AddWithValue("$name", r.Name);
        cmd.Parameters.AddWithValue("$type", (int)r.TradeType);
        cmd.Parameters.AddWithValue("$amt", r.Amount.ToString(CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$cash", (object?)r.CashAccountId?.ToString() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$cat", (object?)r.CategoryId?.ToString() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$freq", (int)r.Frequency);
        cmd.Parameters.AddWithValue("$interval", r.Interval);
        cmd.Parameters.AddWithValue("$start", r.StartDate.ToString("o", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$end", (object?)r.EndDate?.ToString("o", CultureInfo.InvariantCulture) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$mode", (int)r.GenerationMode);
        cmd.Parameters.AddWithValue("$last", (object?)r.LastGeneratedAt?.ToString("o", CultureInfo.InvariantCulture) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$next", (object?)r.NextDueAt?.ToString("o", CultureInfo.InvariantCulture) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$note", (object?)r.Note ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$enabled", r.IsEnabled ? 1 : 0);
    }
}
