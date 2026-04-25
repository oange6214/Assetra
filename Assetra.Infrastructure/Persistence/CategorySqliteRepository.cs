using Microsoft.Data.Sqlite;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;

namespace Assetra.Infrastructure.Persistence;

public sealed class CategorySqliteRepository : ICategoryRepository
{
    private readonly string _connectionString;

    public CategorySqliteRepository(string dbPath)
    {
        _connectionString = $"Data Source={dbPath}";
        CategorySchemaMigrator.EnsureInitialized(_connectionString);
    }

    private const string SelectClause =
        "id, name, kind, parent_id, icon, color_hex, sort_order, is_archived";

    private static ExpenseCategory Map(SqliteDataReader r) => new(
        Id: Guid.Parse(r.GetString(0)),
        Name: r.GetString(1),
        Kind: Enum.Parse<CategoryKind>(r.GetString(2)),
        ParentId: r.IsDBNull(3) ? null : Guid.Parse(r.GetString(3)),
        Icon: r.IsDBNull(4) ? null : r.GetString(4),
        ColorHex: r.IsDBNull(5) ? null : r.GetString(5),
        SortOrder: r.GetInt32(6),
        IsArchived: r.GetInt32(7) != 0);

    public async Task<IReadOnlyList<ExpenseCategory>> GetAllAsync(CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            $"SELECT {SelectClause} FROM expense_category " +
            "ORDER BY kind, sort_order, name;";
        var results = new List<ExpenseCategory>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            results.Add(Map(reader));
        return results;
    }

    public async Task<ExpenseCategory?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {SelectClause} FROM expense_category WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id.ToString());
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false) ? Map(reader) : null;
    }

    public async Task AddAsync(ExpenseCategory c, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(c);
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR IGNORE INTO expense_category
                (id, name, kind, parent_id, icon, color_hex, sort_order, is_archived,
                 created_at, updated_at)
            VALUES
                ($id, $name, $kind, $pid, $icon, $color, $sort, $arch, $now, $now);
            """;
        Bind(cmd, c);
        cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task UpdateAsync(ExpenseCategory c, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(c);
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE expense_category SET
                name        = $name,
                kind        = $kind,
                parent_id   = $pid,
                icon        = $icon,
                color_hex   = $color,
                sort_order  = $sort,
                is_archived = $arch,
                updated_at  = $now
            WHERE id = $id;
            """;
        Bind(cmd, c);
        cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task RemoveAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        // Detach children first (set parent_id = null) so delete doesn't orphan FK references.
        cmd.CommandText = """
            UPDATE expense_category SET parent_id = NULL WHERE parent_id = $id;
            DELETE FROM expense_category WHERE id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", id.ToString());
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<bool> AnyAsync(CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT EXISTS(SELECT 1 FROM expense_category LIMIT 1);";
        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return Convert.ToInt64(result) != 0;
    }

    private static void Bind(SqliteCommand cmd, ExpenseCategory c)
    {
        cmd.Parameters.AddWithValue("$id", c.Id.ToString());
        cmd.Parameters.AddWithValue("$name", c.Name);
        cmd.Parameters.AddWithValue("$kind", c.Kind.ToString());
        cmd.Parameters.AddWithValue("$pid", c.ParentId.HasValue ? (object)c.ParentId.Value.ToString() : DBNull.Value);
        cmd.Parameters.AddWithValue("$icon", c.Icon is not null ? (object)c.Icon : DBNull.Value);
        cmd.Parameters.AddWithValue("$color", c.ColorHex is not null ? (object)c.ColorHex : DBNull.Value);
        cmd.Parameters.AddWithValue("$sort", c.SortOrder);
        cmd.Parameters.AddWithValue("$arch", c.IsArchived ? 1 : 0);
    }
}
