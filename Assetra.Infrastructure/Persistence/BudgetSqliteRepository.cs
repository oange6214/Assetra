using System.Globalization;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Microsoft.Data.Sqlite;

namespace Assetra.Infrastructure.Persistence;

public sealed class BudgetSqliteRepository : IBudgetRepository
{
    private readonly string _connectionString;

    public BudgetSqliteRepository(string dbPath)
    {
        _connectionString = $"Data Source={dbPath}";
        BudgetSchemaMigrator.EnsureInitialized(_connectionString);
    }

    private const string SelectClause =
        "id, category_id, mode, year, month, amount, currency, note";

    private static Budget Map(SqliteDataReader r) => new(
        Id: Guid.Parse(r.GetString(0)),
        CategoryId: r.IsDBNull(1) ? null : Guid.Parse(r.GetString(1)),
        Mode: (BudgetMode)r.GetInt32(2),
        Year: r.GetInt32(3),
        Month: r.IsDBNull(4) ? null : r.GetInt32(4),
        Amount: decimal.Parse(r.GetString(5), CultureInfo.InvariantCulture),
        Currency: r.GetString(6),
        Note: r.IsDBNull(7) ? null : r.GetString(7));

    public async Task<IReadOnlyList<Budget>> GetAllAsync(CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            $"SELECT {SelectClause} FROM budget ORDER BY year DESC, month DESC, category_id;";
        var results = new List<Budget>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            results.Add(Map(reader));
        return results;
    }

    public async Task<Budget?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {SelectClause} FROM budget WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id.ToString());
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false) ? Map(reader) : null;
    }

    public async Task<IReadOnlyList<Budget>> GetByPeriodAsync(int year, int? month, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        if (month.HasValue)
        {
            cmd.CommandText =
                $"SELECT {SelectClause} FROM budget WHERE year = $y AND month = $m;";
            cmd.Parameters.AddWithValue("$y", year);
            cmd.Parameters.AddWithValue("$m", month.Value);
        }
        else
        {
            cmd.CommandText =
                $"SELECT {SelectClause} FROM budget WHERE year = $y AND month IS NULL;";
            cmd.Parameters.AddWithValue("$y", year);
        }
        var results = new List<Budget>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            results.Add(Map(reader));
        return results;
    }

    public async Task AddAsync(Budget budget, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(budget);
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR IGNORE INTO budget
                (id, category_id, mode, year, month, amount, currency, note,
                 created_at, updated_at)
            VALUES
                ($id, $cat, $mode, $y, $m, $amt, $cur, $note, $now, $now);
            """;
        Bind(cmd, budget);
        cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task UpdateAsync(Budget budget, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(budget);
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE budget SET
                category_id = $cat,
                mode        = $mode,
                year        = $y,
                month       = $m,
                amount      = $amt,
                currency    = $cur,
                note        = $note,
                updated_at  = $now
            WHERE id = $id;
            """;
        Bind(cmd, budget);
        cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task RemoveAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM budget WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id.ToString());
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static void Bind(SqliteCommand cmd, Budget b)
    {
        cmd.Parameters.AddWithValue("$id", b.Id.ToString());
        cmd.Parameters.AddWithValue("$cat", (object?)b.CategoryId?.ToString() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$mode", (int)b.Mode);
        cmd.Parameters.AddWithValue("$y", b.Year);
        cmd.Parameters.AddWithValue("$m", (object?)b.Month ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$amt", b.Amount.ToString(CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$cur", b.Currency);
        cmd.Parameters.AddWithValue("$note", (object?)b.Note ?? DBNull.Value);
    }
}
