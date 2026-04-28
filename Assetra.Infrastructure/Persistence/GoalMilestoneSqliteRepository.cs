using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Microsoft.Data.Sqlite;

namespace Assetra.Infrastructure.Persistence;

public sealed class GoalMilestoneSqliteRepository : IGoalMilestoneRepository
{
    private readonly string _connectionString;

    public GoalMilestoneSqliteRepository(string dbPath)
    {
        _connectionString = $"Data Source={dbPath}";
        GoalSchemaMigrator.EnsureInitialized(_connectionString);
    }

    public async Task<IReadOnlyList<GoalMilestone>> GetByGoalAsync(Guid goalId, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, goal_id, target_date, target_amount, label, is_achieved
            FROM goal_milestone
            WHERE goal_id = $gid
            ORDER BY target_date;
            """;
        cmd.Parameters.AddWithValue("$gid", goalId.ToString());
        var results = new List<GoalMilestone>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            results.Add(new GoalMilestone(
                Guid.Parse(reader.GetString(0)),
                Guid.Parse(reader.GetString(1)),
                DateOnly.Parse(reader.GetString(2)),
                (decimal)reader.GetDouble(3),
                reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                reader.GetInt64(5) != 0));
        }
        return results;
    }

    public async Task AddAsync(GoalMilestone milestone, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(milestone);
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO goal_milestone (id, goal_id, target_date, target_amount, label, is_achieved)
            VALUES ($id, $gid, $td, $ta, $lbl, $ach);
            """;
        Bind(cmd, milestone);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task UpdateAsync(GoalMilestone milestone, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(milestone);
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE goal_milestone SET
                target_date   = $td,
                target_amount = $ta,
                label         = $lbl,
                is_achieved   = $ach
            WHERE id = $id;
            """;
        Bind(cmd, milestone);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task RemoveAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM goal_milestone WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id.ToString());
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static void Bind(SqliteCommand cmd, GoalMilestone m)
    {
        cmd.Parameters.AddWithValue("$id", m.Id.ToString());
        cmd.Parameters.AddWithValue("$gid", m.GoalId.ToString());
        cmd.Parameters.AddWithValue("$td", m.TargetDate.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$ta", (double)m.TargetAmount);
        cmd.Parameters.AddWithValue("$lbl", m.Label ?? string.Empty);
        cmd.Parameters.AddWithValue("$ach", m.IsAchieved ? 1 : 0);
    }
}
