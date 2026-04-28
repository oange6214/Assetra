using Assetra.Core.Interfaces.MultiAsset;
using Assetra.Core.Interfaces.Sync;
using Assetra.Core.Models.MultiAsset;
using Assetra.Core.Models.Sync;
using Assetra.Infrastructure.Sync;
using Microsoft.Data.Sqlite;

namespace Assetra.Infrastructure.Persistence;

public sealed class RetirementAccountSqliteRepository : IRetirementAccountRepository, IRetirementAccountSyncStore
{
    private readonly string _connectionString;

    public RetirementAccountSqliteRepository(string dbPath)
    {
        _connectionString = $"Data Source={dbPath}";
        RetirementSchemaMigrator.EnsureInitialized(_connectionString);
    }

    public async Task<IReadOnlyList<RetirementAccount>> GetAllAsync(CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, name, account_type, provider, balance,
                   employee_contribution_rate, employer_contribution_rate,
                   years_of_service, legal_withdrawal_age, opened_date,
                   currency, status, notes,
                   ev_version, ev_modified_at, ev_device_id
            FROM retirement_account
            WHERE is_deleted = 0
            ORDER BY rowid;
            """;
        var results = new List<RetirementAccount>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            results.Add(MapRow(reader));
        return results;
    }

    public async Task<RetirementAccount?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, name, account_type, provider, balance,
                   employee_contribution_rate, employer_contribution_rate,
                   years_of_service, legal_withdrawal_age, opened_date,
                   currency, status, notes,
                   ev_version, ev_modified_at, ev_device_id
            FROM retirement_account WHERE id = $id AND is_deleted = 0;
            """;
        cmd.Parameters.AddWithValue("$id", id.ToString());
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false) ? MapRow(reader) : null;
    }

    public async Task AddAsync(RetirementAccount entity, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO retirement_account
                (id, name, account_type, provider, balance,
                 employee_contribution_rate, employer_contribution_rate,
                 years_of_service, legal_withdrawal_age, opened_date,
                 currency, status, notes,
                 ev_version, ev_modified_at, ev_device_id,
                 is_deleted, is_pending_push, created_at, updated_at)
            VALUES
                ($id, $name, $account_type, $provider, $balance,
                 $employee_rate, $employer_rate,
                 $years_of_service, $legal_age, $opened_date,
                 $currency, $status, $notes,
                 $ev_version, $ev_modified_at, $ev_device_id,
                 0, 1, $now, $now);
            """;
        BindParams(cmd, entity);
        cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task UpdateAsync(RetirementAccount entity, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE retirement_account SET
                name                       = $name,
                account_type               = $account_type,
                provider                   = $provider,
                balance                    = $balance,
                employee_contribution_rate = $employee_rate,
                employer_contribution_rate = $employer_rate,
                years_of_service           = $years_of_service,
                legal_withdrawal_age       = $legal_age,
                opened_date                = $opened_date,
                currency                   = $currency,
                status                     = $status,
                notes                      = $notes,
                ev_version                 = $ev_version,
                ev_modified_at             = $ev_modified_at,
                ev_device_id               = $ev_device_id,
                is_pending_push            = 1,
                updated_at                 = $now
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
            UPDATE retirement_account SET
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
            SELECT id, name, account_type, provider, balance,
                   employee_contribution_rate, employer_contribution_rate,
                   years_of_service, legal_withdrawal_age, opened_date,
                   currency, status, notes,
                   ev_version, ev_modified_at, ev_device_id, is_deleted
            FROM retirement_account WHERE is_pending_push = 1;
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
            var entity = MapRowFull(reader, version);
            results.Add(RetirementAccountSyncMapper.ToEnvelope(entity, isDeleted));
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
        cmd.CommandText = "UPDATE retirement_account SET is_pending_push = 0 WHERE id = $id;";
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
            if (env.EntityType != RetirementAccountSyncMapper.EntityType) continue;

            await using (var probe = conn.CreateCommand())
            {
                probe.Transaction = tx;
                probe.CommandText = "SELECT ev_version FROM retirement_account WHERE id = $id;";
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
                    INSERT INTO retirement_account
                        (id, name, account_type, provider, balance,
                         employee_contribution_rate, employer_contribution_rate,
                         years_of_service, legal_withdrawal_age, opened_date,
                         currency, status,
                         ev_version, ev_modified_at, ev_device_id,
                         is_deleted, is_pending_push, created_at, updated_at)
                    VALUES
                        ($id, '', 'LaborPension', '', 0, 0, 0, 0, 65, '2000-01-01', 'TWD', 'Closed',
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
                var entity = RetirementAccountSyncMapper.FromPayload(env, env.Version);
                await using var upsert = conn.CreateCommand();
                upsert.Transaction = tx;
                upsert.CommandText = """
                    INSERT INTO retirement_account
                        (id, name, account_type, provider, balance,
                         employee_contribution_rate, employer_contribution_rate,
                         years_of_service, legal_withdrawal_age, opened_date,
                         currency, status, notes,
                         ev_version, ev_modified_at, ev_device_id,
                         is_deleted, is_pending_push, created_at, updated_at)
                    VALUES
                        ($id, $name, $account_type, $provider, $balance,
                         $employee_rate, $employer_rate,
                         $years_of_service, $legal_age, $opened_date,
                         $currency, $status, $notes,
                         $ev_version, $ev_modified_at, $ev_device_id,
                         0, 0, $now, $now)
                    ON CONFLICT(id) DO UPDATE SET
                        name                       = excluded.name,
                        account_type               = excluded.account_type,
                        provider                   = excluded.provider,
                        balance                    = excluded.balance,
                        employee_contribution_rate = excluded.employee_contribution_rate,
                        employer_contribution_rate = excluded.employer_contribution_rate,
                        years_of_service           = excluded.years_of_service,
                        legal_withdrawal_age       = excluded.legal_withdrawal_age,
                        opened_date                = excluded.opened_date,
                        currency                   = excluded.currency,
                        status                     = excluded.status,
                        notes                      = excluded.notes,
                        ev_version                 = excluded.ev_version,
                        ev_modified_at             = excluded.ev_modified_at,
                        ev_device_id               = excluded.ev_device_id,
                        is_deleted                 = 0,
                        is_pending_push            = 0,
                        updated_at                 = excluded.updated_at;
                    """;
                BindParams(upsert, entity);
                upsert.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
                await upsert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
        }
        await tx.CommitAsync(ct).ConfigureAwait(false);
    }

    private static RetirementAccount MapRow(SqliteDataReader r)
    {
        var version = new EntityVersion(
            r.GetInt64(13),
            r.IsDBNull(14) || r.GetString(14).Length == 0
                ? default
                : DateTimeOffset.Parse(r.GetString(14)),
            r.IsDBNull(15) ? string.Empty : r.GetString(15));
        return MapRowFull(r, version);
    }

    private static RetirementAccount MapRowFull(SqliteDataReader r, EntityVersion version) =>
        new(
            Id: Guid.Parse(r.GetString(0)),
            Name: r.GetString(1),
            AccountType: Enum.Parse<RetirementAccountType>(r.GetString(2)),
            Provider: r.GetString(3),
            Balance: (decimal)r.GetDouble(4),
            EmployeeContributionRate: (decimal)r.GetDouble(5),
            EmployerContributionRate: (decimal)r.GetDouble(6),
            YearsOfService: (int)r.GetInt64(7),
            LegalWithdrawalAge: (int)r.GetInt64(8),
            OpenedDate: DateOnly.Parse(r.GetString(9)),
            Currency: r.GetString(10),
            Status: Enum.Parse<RetirementAccountStatus>(r.GetString(11)),
            Notes: r.IsDBNull(12) ? null : r.GetString(12),
            Version: version);

    private static void BindParams(SqliteCommand cmd, RetirementAccount e)
    {
        cmd.Parameters.AddWithValue("$id", e.Id.ToString());
        cmd.Parameters.AddWithValue("$name", e.Name);
        cmd.Parameters.AddWithValue("$account_type", e.AccountType.ToString());
        cmd.Parameters.AddWithValue("$provider", e.Provider);
        cmd.Parameters.AddWithValue("$balance", (double)e.Balance);
        cmd.Parameters.AddWithValue("$employee_rate", (double)e.EmployeeContributionRate);
        cmd.Parameters.AddWithValue("$employer_rate", (double)e.EmployerContributionRate);
        cmd.Parameters.AddWithValue("$years_of_service", e.YearsOfService);
        cmd.Parameters.AddWithValue("$legal_age", e.LegalWithdrawalAge);
        cmd.Parameters.AddWithValue("$opened_date", e.OpenedDate.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$currency", e.Currency);
        cmd.Parameters.AddWithValue("$status", e.Status.ToString());
        cmd.Parameters.AddWithValue("$notes", e.Notes ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$ev_version", e.Version.Version);
        cmd.Parameters.AddWithValue("$ev_modified_at", e.Version.LastModifiedAt == default
            ? string.Empty : e.Version.LastModifiedAt.ToString("o"));
        cmd.Parameters.AddWithValue("$ev_device_id", e.Version.LastModifiedByDevice);
    }
}
