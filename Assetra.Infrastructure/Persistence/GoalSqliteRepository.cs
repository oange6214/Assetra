using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Microsoft.Data.Sqlite;

namespace Assetra.Infrastructure.Persistence;

public sealed class GoalSqliteRepository : IFinancialGoalRepository
{
    private readonly string _connectionString;

    public GoalSqliteRepository(string dbPath)
    {
        _connectionString = $"Data Source={dbPath}";
        GoalSchemaMigrator.EnsureInitialized(_connectionString);
    }

    public async Task<IReadOnlyList<FinancialGoal>> GetAllAsync(CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, name, target_amount, current_amount, deadline, notes
            FROM financial_goal
            ORDER BY rowid;
            """;
        var results = new List<FinancialGoal>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            DateOnly? deadline = reader.IsDBNull(4)
                ? null
                : DateOnly.Parse(reader.GetString(4));
            results.Add(new FinancialGoal(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1),
                (decimal)reader.GetDouble(2),
                (decimal)reader.GetDouble(3),
                deadline,
                reader.IsDBNull(5) ? null : reader.GetString(5)));
        }
        return results;
    }

    public async Task AddAsync(FinancialGoal goal, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO financial_goal
                (id, name, target_amount, current_amount, deadline, notes, created_at, updated_at)
            VALUES
                ($id, $name, $target, $current, $deadline, $notes, $created_at, $updated_at);
            """;
        BindGoalParams(cmd, goal);
        var now = DateTime.UtcNow.ToString("o");
        cmd.Parameters.AddWithValue("$created_at", now);
        cmd.Parameters.AddWithValue("$updated_at", now);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task UpdateAsync(FinancialGoal goal, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE financial_goal SET
                name           = $name,
                target_amount  = $target,
                current_amount = $current,
                deadline       = $deadline,
                notes          = $notes,
                updated_at     = $updated_at
            WHERE id = $id;
            """;
        BindGoalParams(cmd, goal);
        cmd.Parameters.AddWithValue("$updated_at", DateTime.UtcNow.ToString("o"));
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task RemoveAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await EnableForeignKeysAsync(conn, ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM financial_goal WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id.ToString());
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task EnableForeignKeysAsync(SqliteConnection conn, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA foreign_keys = ON;";
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static void BindGoalParams(SqliteCommand cmd, FinancialGoal goal)
    {
        cmd.Parameters.AddWithValue("$id", goal.Id.ToString());
        cmd.Parameters.AddWithValue("$name", goal.Name);
        cmd.Parameters.AddWithValue("$target", (double)goal.TargetAmount);
        cmd.Parameters.AddWithValue("$current", (double)goal.CurrentAmount);
        cmd.Parameters.AddWithValue("$deadline",
            goal.Deadline?.ToString("yyyy-MM-dd") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$notes", goal.Notes ?? (object)DBNull.Value);
    }
}
