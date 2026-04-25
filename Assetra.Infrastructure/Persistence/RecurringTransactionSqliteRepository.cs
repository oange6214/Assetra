using System.Globalization;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Microsoft.Data.Sqlite;

namespace Assetra.Infrastructure.Persistence;

public sealed class RecurringTransactionSqliteRepository : IRecurringTransactionRepository
{
    private readonly string _connectionString;

    public RecurringTransactionSqliteRepository(string dbPath)
    {
        _connectionString = $"Data Source={dbPath}";
        RecurringSchemaMigrator.EnsureInitialized(_connectionString);
    }

    private const string SelectClause =
        "id, name, trade_type, amount, cash_account_id, category_id, frequency, interval_value, " +
        "start_date, end_date, generation_mode, last_generated_at, next_due_at, note, is_enabled";

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
        cmd.CommandText = $"SELECT {SelectClause} FROM recurring_transaction ORDER BY name;";
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
        cmd.CommandText = $"SELECT {SelectClause} FROM recurring_transaction WHERE is_enabled = 1 ORDER BY next_due_at;";
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
        cmd.CommandText = $"SELECT {SelectClause} FROM recurring_transaction WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id.ToString());
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false) ? Map(reader) : null;
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
                 created_at, updated_at)
            VALUES
                ($id, $name, $type, $amt, $cash, $cat, $freq, $interval,
                 $start, $end, $mode, $last, $next, $note, $enabled, $now, $now);
            """;
        Bind(cmd, recurring);
        cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
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
                updated_at        = $now
            WHERE id = $id;
            """;
        Bind(cmd, recurring);
        cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task RemoveAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM recurring_transaction WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id.ToString());
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
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
