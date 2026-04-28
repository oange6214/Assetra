using Assetra.Core.Interfaces.MultiAsset;
using Assetra.Core.Models.MultiAsset;
using Microsoft.Data.Sqlite;

namespace Assetra.Infrastructure.Persistence;

public sealed class RentalIncomeRecordSqliteRepository : IRentalIncomeRecordRepository
{
    private readonly string _connectionString;

    public RentalIncomeRecordSqliteRepository(string dbPath)
    {
        _connectionString = $"Data Source={dbPath}";
        // Schema initialized by RealEstateSqliteRepository constructor
    }

    public async Task<IReadOnlyList<RentalIncomeRecord>> GetByPropertyAsync(
        Guid realEstateId, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, real_estate_id, month, rent_amount, expenses, currency, notes
            FROM rental_income_record
            WHERE real_estate_id = $id
            ORDER BY month;
            """;
        cmd.Parameters.AddWithValue("$id", realEstateId.ToString());
        return await ReadRecordsAsync(cmd, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<RentalIncomeRecord>> GetByPeriodAsync(
        DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, real_estate_id, month, rent_amount, expenses, currency, notes
            FROM rental_income_record
            WHERE month >= $from AND month <= $to
            ORDER BY month;
            """;
        cmd.Parameters.AddWithValue("$from", from.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$to", to.ToString("yyyy-MM-dd"));
        return await ReadRecordsAsync(cmd, ct).ConfigureAwait(false);
    }

    public async Task AddAsync(RentalIncomeRecord record, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO rental_income_record
                (id, real_estate_id, month, rent_amount, expenses, currency, notes)
            VALUES
                ($id, $real_estate_id, $month, $rent_amount, $expenses, $currency, $notes);
            """;
        BindParams(cmd, record);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task UpdateAsync(RentalIncomeRecord record, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE rental_income_record SET
                month       = $month,
                rent_amount = $rent_amount,
                expenses    = $expenses,
                currency    = $currency,
                notes       = $notes
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
        cmd.CommandText = "DELETE FROM rental_income_record WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id.ToString());
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<RentalIncomeRecord>> ReadRecordsAsync(
        SqliteCommand cmd, CancellationToken ct)
    {
        var results = new List<RentalIncomeRecord>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            results.Add(new RentalIncomeRecord(
                Id: Guid.Parse(reader.GetString(0)),
                RealEstateId: Guid.Parse(reader.GetString(1)),
                Month: DateOnly.Parse(reader.GetString(2)),
                RentAmount: (decimal)reader.GetDouble(3),
                Expenses: (decimal)reader.GetDouble(4),
                Currency: reader.GetString(5),
                Notes: reader.IsDBNull(6) ? null : reader.GetString(6)));
        }
        return results;
    }

    private static void BindParams(SqliteCommand cmd, RentalIncomeRecord r)
    {
        cmd.Parameters.AddWithValue("$id", r.Id.ToString());
        cmd.Parameters.AddWithValue("$real_estate_id", r.RealEstateId.ToString());
        cmd.Parameters.AddWithValue("$month", r.Month.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$rent_amount", (double)r.RentAmount);
        cmd.Parameters.AddWithValue("$expenses", (double)r.Expenses);
        cmd.Parameters.AddWithValue("$currency", r.Currency);
        cmd.Parameters.AddWithValue("$notes", r.Notes ?? (object)DBNull.Value);
    }
}
