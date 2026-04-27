using Microsoft.Data.Sqlite;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;

namespace Assetra.Infrastructure.Persistence;

public sealed class AutoCategorizationRuleSqliteRepository : IAutoCategorizationRuleRepository
{
    private readonly string _connectionString;

    public AutoCategorizationRuleSqliteRepository(string dbPath)
    {
        _connectionString = $"Data Source={dbPath}";
        AutoCategorizationRuleSchemaMigrator.EnsureInitialized(_connectionString);
    }

    private const string SelectClause =
        "id, keyword_pattern, category_id, priority, is_enabled, match_case_sensitive, " +
        "name, match_field, match_type, applies_to";

    private static AutoCategorizationRule Map(SqliteDataReader r) => new(
        Id: Guid.Parse(r.GetString(0)),
        KeywordPattern: r.GetString(1),
        CategoryId: Guid.Parse(r.GetString(2)),
        Priority: r.GetInt32(3),
        IsEnabled: r.GetInt32(4) != 0,
        MatchCaseSensitive: r.GetInt32(5) != 0,
        Name: r.IsDBNull(6) ? null : r.GetString(6),
        MatchField: (AutoCategorizationMatchField)r.GetInt32(7),
        MatchType: (AutoCategorizationMatchType)r.GetInt32(8),
        AppliesTo: (AutoCategorizationScope)r.GetInt32(9));

    public async Task<IReadOnlyList<AutoCategorizationRule>> GetAllAsync(CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            $"SELECT {SelectClause} FROM auto_categorization_rule " +
            "ORDER BY priority, keyword_pattern;";
        var results = new List<AutoCategorizationRule>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            results.Add(Map(reader));
        return results;
    }

    public async Task<AutoCategorizationRule?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {SelectClause} FROM auto_categorization_rule WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id.ToString());
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false) ? Map(reader) : null;
    }

    public async Task AddAsync(AutoCategorizationRule rule, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(rule);
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR IGNORE INTO auto_categorization_rule
                (id, keyword_pattern, category_id, priority, is_enabled,
                 match_case_sensitive, created_at, updated_at,
                 name, match_field, match_type, applies_to)
            VALUES
                ($id, $kw, $cat, $pri, $en,
                 $cs, $now, $now,
                 $name, $field, $type, $scope);
            """;
        Bind(cmd, rule);
        cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task UpdateAsync(AutoCategorizationRule rule, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(rule);
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE auto_categorization_rule SET
                keyword_pattern      = $kw,
                category_id          = $cat,
                priority             = $pri,
                is_enabled           = $en,
                match_case_sensitive = $cs,
                name                 = $name,
                match_field          = $field,
                match_type           = $type,
                applies_to           = $scope,
                updated_at           = $now
            WHERE id = $id;
            """;
        Bind(cmd, rule);
        cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task RemoveAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM auto_categorization_rule WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id.ToString());
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static void Bind(SqliteCommand cmd, AutoCategorizationRule r)
    {
        cmd.Parameters.AddWithValue("$id", r.Id.ToString());
        cmd.Parameters.AddWithValue("$kw", r.KeywordPattern);
        cmd.Parameters.AddWithValue("$cat", r.CategoryId.ToString());
        cmd.Parameters.AddWithValue("$pri", r.Priority);
        cmd.Parameters.AddWithValue("$en", r.IsEnabled ? 1 : 0);
        cmd.Parameters.AddWithValue("$cs", r.MatchCaseSensitive ? 1 : 0);
        cmd.Parameters.AddWithValue("$name", (object?)r.Name ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$field", (int)r.MatchField);
        cmd.Parameters.AddWithValue("$type", (int)r.MatchType);
        cmd.Parameters.AddWithValue("$scope", (int)r.AppliesTo);
    }
}
