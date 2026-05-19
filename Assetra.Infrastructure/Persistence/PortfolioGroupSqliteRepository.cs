using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Microsoft.Data.Sqlite;

namespace Assetra.Infrastructure.Persistence;

public sealed class PortfolioGroupSqliteRepository : IPortfolioGroupRepository
{
    private readonly string _connectionString;

    public PortfolioGroupSqliteRepository(string dbPath)
    {
        _connectionString = $"Data Source={dbPath}";
        PortfolioGroupSchemaMigrator.EnsureInitialized(_connectionString);
    }

    private const string SelectClause =
        "id, name, color_hex, description, icon_key, sort_order, default_cash_account_id, is_system";

    public async Task<IReadOnlyList<PortfolioGroup>> GetAllAsync(CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {SelectClause} FROM portfolio_group ORDER BY sort_order, created_at;";
        var results = new List<PortfolioGroup>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            results.Add(MapGroup(reader));
        return results;
    }

    public async Task<PortfolioGroup?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {SelectClause} FROM portfolio_group WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id.ToString());
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false) ? MapGroup(reader) : null;
    }

    public async Task AddAsync(PortfolioGroup group, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(group);
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO portfolio_group
                (id, name, color_hex, description, icon_key, sort_order, default_cash_account_id, is_system, created_at, updated_at)
            VALUES
                ($id, $name, $color, $desc, $icon, $sort, $cash, $sys, $now, $now);
            """;
        BindGroupParams(cmd, group);
        cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task UpdateAsync(PortfolioGroup group, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(group);
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        // Note: is_system is intentionally NOT in the SET clause — system flag
        // is set once at migration seed time and never flipped post-hoc.
        cmd.CommandText = """
            UPDATE portfolio_group SET
                name                    = $name,
                color_hex               = $color,
                description             = $desc,
                icon_key                = $icon,
                sort_order              = $sort,
                default_cash_account_id = $cash,
                updated_at              = $now
            WHERE id = $id;
            """;
        BindGroupParams(cmd, group);
        cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task RemoveAsync(Guid id, CancellationToken ct = default)
    {
        // Guard：system-protected group cannot be deleted. Read flag first then
        // throw before issuing DELETE so caller gets a clear error.
        var existing = await GetByIdAsync(id, ct).ConfigureAwait(false);
        if (existing is null)
            return;
        if (existing.IsSystem)
            throw new InvalidOperationException($"Portfolio group '{existing.Name}' is system-protected and cannot be deleted.");

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM portfolio_group WHERE id = $id AND is_system = 0;";
        cmd.Parameters.AddWithValue("$id", id.ToString());
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static PortfolioGroup MapGroup(SqliteDataReader r) => new(
        Id:                    Guid.Parse(r.GetString(0)),
        Name:                  r.GetString(1),
        ColorHex:              r.IsDBNull(2) ? null : r.GetString(2),
        Description:           r.IsDBNull(3) ? null : r.GetString(3),
        IconKey:               r.IsDBNull(4) ? null : r.GetString(4),
        SortOrder:             r.GetInt32(5),
        DefaultCashAccountId:  r.IsDBNull(6) ? null : Guid.Parse(r.GetString(6)),
        IsSystem:              r.GetInt32(7) != 0);

    private static void BindGroupParams(SqliteCommand cmd, PortfolioGroup g)
    {
        cmd.Parameters.AddWithValue("$id", g.Id.ToString());
        cmd.Parameters.AddWithValue("$name", g.Name);
        cmd.Parameters.AddWithValue("$color", (object?)g.ColorHex ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$desc", (object?)g.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$icon", (object?)g.IconKey ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$sort", g.SortOrder);
        cmd.Parameters.AddWithValue("$cash",
            g.DefaultCashAccountId.HasValue ? (object)g.DefaultCashAccountId.Value.ToString() : DBNull.Value);
        cmd.Parameters.AddWithValue("$sys", g.IsSystem ? 1 : 0);
    }
}
