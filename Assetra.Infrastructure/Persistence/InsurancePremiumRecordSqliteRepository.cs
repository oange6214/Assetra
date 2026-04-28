using Assetra.Core.Interfaces.MultiAsset;
using Assetra.Core.Models.MultiAsset;
using Microsoft.Data.Sqlite;

namespace Assetra.Infrastructure.Persistence;

public sealed class InsurancePremiumRecordSqliteRepository : IInsurancePremiumRecordRepository
{
    private readonly string _connectionString;

    public InsurancePremiumRecordSqliteRepository(string dbPath)
    {
        _connectionString = $"Data Source={dbPath}";
        // Schema initialized by InsurancePolicySqliteRepository constructor
    }

    public async Task<IReadOnlyList<InsurancePremiumRecord>> GetByPolicyAsync(
        Guid policyId, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, policy_id, paid_date, amount, currency, notes
            FROM insurance_premium_record
            WHERE policy_id = $id
            ORDER BY paid_date;
            """;
        cmd.Parameters.AddWithValue("$id", policyId.ToString());
        return await ReadRecordsAsync(cmd, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<InsurancePremiumRecord>> GetByPeriodAsync(
        DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, policy_id, paid_date, amount, currency, notes
            FROM insurance_premium_record
            WHERE paid_date >= $from AND paid_date <= $to
            ORDER BY paid_date;
            """;
        cmd.Parameters.AddWithValue("$from", from.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$to", to.ToString("yyyy-MM-dd"));
        return await ReadRecordsAsync(cmd, ct).ConfigureAwait(false);
    }

    public async Task AddAsync(InsurancePremiumRecord record, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO insurance_premium_record
                (id, policy_id, paid_date, amount, currency, notes)
            VALUES
                ($id, $policy_id, $paid_date, $amount, $currency, $notes);
            """;
        BindParams(cmd, record);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task RemoveAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM insurance_premium_record WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id.ToString());
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<InsurancePremiumRecord>> ReadRecordsAsync(
        SqliteCommand cmd, CancellationToken ct)
    {
        var results = new List<InsurancePremiumRecord>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            results.Add(new InsurancePremiumRecord(
                Id: Guid.Parse(reader.GetString(0)),
                PolicyId: Guid.Parse(reader.GetString(1)),
                PaidDate: DateOnly.Parse(reader.GetString(2)),
                Amount: (decimal)reader.GetDouble(3),
                Currency: reader.GetString(4),
                Notes: reader.IsDBNull(5) ? null : reader.GetString(5)));
        }
        return results;
    }

    private static void BindParams(SqliteCommand cmd, InsurancePremiumRecord r)
    {
        cmd.Parameters.AddWithValue("$id", r.Id.ToString());
        cmd.Parameters.AddWithValue("$policy_id", r.PolicyId.ToString());
        cmd.Parameters.AddWithValue("$paid_date", r.PaidDate.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$amount", (double)r.Amount);
        cmd.Parameters.AddWithValue("$currency", r.Currency);
        cmd.Parameters.AddWithValue("$notes", r.Notes ?? (object)DBNull.Value);
    }
}
