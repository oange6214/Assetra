using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Microsoft.Data.Sqlite;

namespace Assetra.Infrastructure.Persistence;

public sealed class GoalFundingRuleSqliteRepository : IGoalFundingRuleRepository
{
    private readonly string _connectionString;

    public GoalFundingRuleSqliteRepository(string dbPath)
    {
        _connectionString = $"Data Source={dbPath}";
        GoalSchemaMigrator.EnsureInitialized(_connectionString);
    }

    public async Task<IReadOnlyList<GoalFundingRule>> GetByGoalAsync(Guid goalId, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, goal_id, amount, frequency, source_cash_account_id, start_date, end_date, is_enabled
            FROM goal_funding_rule
            WHERE goal_id = $gid
            ORDER BY start_date;
            """;
        cmd.Parameters.AddWithValue("$gid", goalId.ToString());
        return await ReadAllAsync(cmd, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<GoalFundingRule>> GetAllAsync(CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, goal_id, amount, frequency, source_cash_account_id, start_date, end_date, is_enabled
            FROM goal_funding_rule
            ORDER BY rowid;
            """;
        return await ReadAllAsync(cmd, ct).ConfigureAwait(false);
    }

    public async Task AddAsync(GoalFundingRule rule, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(rule);
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO goal_funding_rule
                (id, goal_id, amount, frequency, source_cash_account_id, start_date, end_date, is_enabled)
            VALUES
                ($id, $gid, $amt, $freq, $src, $sd, $ed, $en);
            """;
        Bind(cmd, rule);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task UpdateAsync(GoalFundingRule rule, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(rule);
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE goal_funding_rule SET
                amount                 = $amt,
                frequency              = $freq,
                source_cash_account_id = $src,
                start_date             = $sd,
                end_date               = $ed,
                is_enabled             = $en
            WHERE id = $id;
            """;
        Bind(cmd, rule);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task RemoveAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM goal_funding_rule WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id.ToString());
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<GoalFundingRule>> ReadAllAsync(SqliteCommand cmd, CancellationToken ct)
    {
        var results = new List<GoalFundingRule>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var freq = Enum.TryParse<RecurrenceFrequency>(reader.GetString(3), ignoreCase: true, out var f)
                ? f
                : RecurrenceFrequency.Monthly;
            Guid? sourceId = reader.IsDBNull(4) ? null : Guid.Parse(reader.GetString(4));
            DateOnly? endDate = reader.IsDBNull(6) ? null : DateOnly.Parse(reader.GetString(6));
            results.Add(new GoalFundingRule(
                Guid.Parse(reader.GetString(0)),
                Guid.Parse(reader.GetString(1)),
                (decimal)reader.GetDouble(2),
                freq,
                sourceId,
                DateOnly.Parse(reader.GetString(5)),
                endDate,
                reader.GetInt64(7) != 0));
        }
        return results;
    }

    private static void Bind(SqliteCommand cmd, GoalFundingRule r)
    {
        cmd.Parameters.AddWithValue("$id", r.Id.ToString());
        cmd.Parameters.AddWithValue("$gid", r.GoalId.ToString());
        cmd.Parameters.AddWithValue("$amt", (double)r.Amount);
        cmd.Parameters.AddWithValue("$freq", r.Frequency.ToString());
        cmd.Parameters.AddWithValue("$src", (object?)r.SourceCashAccountId?.ToString() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$sd", r.StartDate.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$ed", r.EndDate?.ToString("yyyy-MM-dd") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$en", r.IsEnabled ? 1 : 0);
    }
}
