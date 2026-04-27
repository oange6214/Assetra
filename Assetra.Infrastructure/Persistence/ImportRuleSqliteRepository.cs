using Assetra.Core.Interfaces.Import;
using Assetra.Core.Models.Import;
using Microsoft.Data.Sqlite;

namespace Assetra.Infrastructure.Persistence;

public sealed class ImportRuleSqliteRepository : IImportRuleRepository
{
    private readonly string _connectionString;

    public ImportRuleSqliteRepository(string dbPath)
    {
        _connectionString = $"Data Source={dbPath}";
        ImportRuleSchemaMigrator.EnsureInitialized(_connectionString);
    }

    private const string SelectClause =
        "id, name, match_field, match_type, pattern, case_sensitive, category_id, " +
        "priority, is_enabled, created_at, updated_at";

    public async Task<IReadOnlyList<ImportRule>> GetAllAsync(CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {SelectClause} FROM import_rule ORDER BY priority ASC, created_at ASC;";
        var results = new List<ImportRule>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            results.Add(Map(reader));
        return results;
    }

    public async Task<ImportRule?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {SelectClause} FROM import_rule WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id.ToString());
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false) ? Map(reader) : null;
    }

    public async Task AddAsync(ImportRule rule, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(rule);
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO import_rule
                (id, name, match_field, match_type, pattern, case_sensitive,
                 category_id, priority, is_enabled, created_at, updated_at)
            VALUES
                ($id, $name, $field, $type, $pattern, $cs,
                 $cat, $prio, $enabled, $created, $updated);
            """;
        BindAll(cmd, rule);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task UpdateAsync(ImportRule rule, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(rule);
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE import_rule SET
                name = $name,
                match_field = $field,
                match_type = $type,
                pattern = $pattern,
                case_sensitive = $cs,
                category_id = $cat,
                priority = $prio,
                is_enabled = $enabled,
                created_at = $created,
                updated_at = $updated
            WHERE id = $id;
            """;
        BindAll(cmd, rule);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task RemoveAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM import_rule WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id.ToString());
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static void BindAll(SqliteCommand cmd, ImportRule rule)
    {
        cmd.Parameters.AddWithValue("$id", rule.Id.ToString());
        cmd.Parameters.AddWithValue("$name", rule.Name);
        cmd.Parameters.AddWithValue("$field", (int)rule.MatchField);
        cmd.Parameters.AddWithValue("$type", (int)rule.MatchType);
        cmd.Parameters.AddWithValue("$pattern", rule.Pattern);
        cmd.Parameters.AddWithValue("$cs", rule.CaseSensitive ? 1 : 0);
        cmd.Parameters.AddWithValue("$cat", rule.CategoryId.ToString());
        cmd.Parameters.AddWithValue("$prio", rule.Priority);
        cmd.Parameters.AddWithValue("$enabled", rule.IsEnabled ? 1 : 0);
        cmd.Parameters.AddWithValue("$created", rule.CreatedAt.ToString("o"));
        cmd.Parameters.AddWithValue("$updated", rule.UpdatedAt.ToString("o"));
    }

    private static ImportRule Map(SqliteDataReader r) => new(
        Id: Guid.Parse(r.GetString(0)),
        Name: r.GetString(1),
        MatchField: (ImportRuleMatchField)r.GetInt32(2),
        MatchType: (ImportRuleMatchType)r.GetInt32(3),
        Pattern: r.GetString(4),
        CaseSensitive: r.GetInt32(5) != 0,
        CategoryId: Guid.Parse(r.GetString(6)),
        Priority: r.GetInt32(7),
        IsEnabled: r.GetInt32(8) != 0,
        CreatedAt: DateTimeOffset.Parse(r.GetString(9)),
        UpdatedAt: DateTimeOffset.Parse(r.GetString(10)));
}
