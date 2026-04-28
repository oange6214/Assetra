using Assetra.Core.Interfaces.MultiAsset;
using Assetra.Core.Models.MultiAsset;
using Microsoft.Data.Sqlite;

namespace Assetra.Infrastructure.Persistence;

public sealed class RetirementContributionSqliteRepository : IRetirementContributionRepository
{
    private readonly string _connectionString;

    public RetirementContributionSqliteRepository(string dbPath)
    {
        _connectionString = $"Data Source={dbPath}";
        // Schema initialized by RetirementAccountSqliteRepository constructor
    }

    public async Task<IReadOnlyList<RetirementContribution>> GetByAccountAsync(
        Guid accountId, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, account_id, year, employee_amount, employer_amount, currency, notes
            FROM retirement_contribution
            WHERE account_id = $id
            ORDER BY year;
            """;
        cmd.Parameters.AddWithValue("$id", accountId.ToString());
        return await ReadRecordsAsync(cmd, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<RetirementContribution>> GetByYearAsync(
        int year, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, account_id, year, employee_amount, employer_amount, currency, notes
            FROM retirement_contribution
            WHERE year = $year
            ORDER BY rowid;
            """;
        cmd.Parameters.AddWithValue("$year", year);
        return await ReadRecordsAsync(cmd, ct).ConfigureAwait(false);
    }

    public async Task AddAsync(RetirementContribution record, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO retirement_contribution
                (id, account_id, year, employee_amount, employer_amount, currency, notes)
            VALUES
                ($id, $account_id, $year, $employee_amount, $employer_amount, $currency, $notes);
            """;
        BindParams(cmd, record);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task UpdateAsync(RetirementContribution record, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE retirement_contribution SET
                year             = $year,
                employee_amount  = $employee_amount,
                employer_amount  = $employer_amount,
                currency         = $currency,
                notes            = $notes
            WHERE id = $id;
            """;
        BindParams(cmd, record);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task RemoveAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM retirement_contribution WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id.ToString());
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<RetirementContribution>> ReadRecordsAsync(
        SqliteCommand cmd, CancellationToken ct)
    {
        var results = new List<RetirementContribution>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            results.Add(new RetirementContribution(
                Id: Guid.Parse(reader.GetString(0)),
                AccountId: Guid.Parse(reader.GetString(1)),
                Year: (int)reader.GetInt64(2),
                EmployeeAmount: (decimal)reader.GetDouble(3),
                EmployerAmount: (decimal)reader.GetDouble(4),
                Currency: reader.GetString(5),
                Notes: reader.IsDBNull(6) ? null : reader.GetString(6)));
        }
        return results;
    }

    private static void BindParams(SqliteCommand cmd, RetirementContribution r)
    {
        cmd.Parameters.AddWithValue("$id", r.Id.ToString());
        cmd.Parameters.AddWithValue("$account_id", r.AccountId.ToString());
        cmd.Parameters.AddWithValue("$year", r.Year);
        cmd.Parameters.AddWithValue("$employee_amount", (double)r.EmployeeAmount);
        cmd.Parameters.AddWithValue("$employer_amount", (double)r.EmployerAmount);
        cmd.Parameters.AddWithValue("$currency", r.Currency);
        cmd.Parameters.AddWithValue("$notes", r.Notes ?? (object)DBNull.Value);
    }
}
