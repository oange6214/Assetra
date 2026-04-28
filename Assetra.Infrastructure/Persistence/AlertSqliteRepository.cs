using Microsoft.Data.Sqlite;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;

namespace Assetra.Infrastructure.Persistence;

public sealed class AlertSqliteRepository : IAlertRepository
{
    private readonly string _connectionString;

    public AlertSqliteRepository(string dbPath)
    {
        _connectionString = $"Data Source={dbPath}";
        AlertSchemaMigrator.EnsureInitialized(_connectionString);
    }

    public async Task<IReadOnlyList<AlertRule>> GetRulesAsync(CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, symbol, exchange, condition, target_price, is_triggered, trigger_time FROM alert ORDER BY rowid;";
        var results = new List<AlertRule>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var triggerTimeStr = reader.IsDBNull(6) ? null : reader.GetString(6);
            results.Add(new AlertRule(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1),
                reader.GetString(2),
                (AlertCondition)reader.GetInt32(3),
                (decimal)reader.GetDouble(4),
                reader.GetInt32(5) != 0,
                triggerTimeStr is null ? null : DateTimeOffset.Parse(triggerTimeStr)));
        }
        return results;
    }

    public async Task AddAsync(AlertRule rule, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR IGNORE INTO alert (id, symbol, exchange, condition, target_price, is_triggered, trigger_time, created_at, updated_at)
            VALUES ($id, $sym, $ex, $cond, $tp, $trig, $tt, $created_at, $updated_at);
            """;
        cmd.Parameters.AddWithValue("$id", rule.Id.ToString());
        cmd.Parameters.AddWithValue("$sym", rule.Symbol);
        cmd.Parameters.AddWithValue("$ex", rule.Exchange);
        cmd.Parameters.AddWithValue("$cond", (int)rule.Condition);
        cmd.Parameters.AddWithValue("$tp", (double)rule.TargetPrice);
        cmd.Parameters.AddWithValue("$trig", rule.IsTriggered ? 1 : 0);
        cmd.Parameters.AddWithValue("$tt", rule.TriggerTime?.ToString("O") ?? (object)DBNull.Value);
        var now = DateTime.UtcNow.ToString("o");
        cmd.Parameters.AddWithValue("$created_at", now);
        cmd.Parameters.AddWithValue("$updated_at", now);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task RemoveAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM alert WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id.ToString());
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task UpdateAsync(AlertRule rule, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE alert SET
                condition    = $cond,
                target_price = $tp,
                is_triggered = $trig,
                trigger_time = $tt,
                updated_at   = $updated_at
            WHERE id = $id;
            """;
        cmd.Parameters.AddWithValue("$cond", (int)rule.Condition);
        cmd.Parameters.AddWithValue("$tp", (double)rule.TargetPrice);
        cmd.Parameters.AddWithValue("$trig", rule.IsTriggered ? 1 : 0);
        cmd.Parameters.AddWithValue("$tt", rule.TriggerTime?.ToString("O") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$id", rule.Id.ToString());
        cmd.Parameters.AddWithValue("$updated_at", DateTime.UtcNow.ToString("o"));
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}
