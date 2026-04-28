using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Microsoft.Data.Sqlite;

namespace Assetra.Infrastructure.Persistence;

public sealed class PortfolioEventSqliteRepository : IPortfolioEventRepository
{
    private readonly string _connectionString;

    public PortfolioEventSqliteRepository(string dbPath)
    {
        _connectionString = $"Data Source={dbPath}";
        PortfolioEventSchemaMigrator.EnsureInitialized(_connectionString);
    }

    public async Task<IReadOnlyList<PortfolioEvent>> GetAllAsync(CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, event_date, kind, label, description, amount, symbol
            FROM portfolio_event
            ORDER BY event_date;
            """;
        return await ReadAllAsync(cmd, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PortfolioEvent>> GetRangeAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, event_date, kind, label, description, amount, symbol
            FROM portfolio_event
            WHERE event_date >= $f AND event_date <= $t
            ORDER BY event_date;
            """;
        cmd.Parameters.AddWithValue("$f", from.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$t", to.ToString("yyyy-MM-dd"));
        return await ReadAllAsync(cmd, ct).ConfigureAwait(false);
    }

    public async Task AddAsync(PortfolioEvent evt, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(evt);
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO portfolio_event (id, event_date, kind, label, description, amount, symbol)
            VALUES ($id, $d, $k, $l, $desc, $amt, $sym);
            """;
        Bind(cmd, evt);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task UpdateAsync(PortfolioEvent evt, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(evt);
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE portfolio_event SET
                event_date  = $d,
                kind        = $k,
                label       = $l,
                description = $desc,
                amount      = $amt,
                symbol      = $sym
            WHERE id = $id;
            """;
        Bind(cmd, evt);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task RemoveAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM portfolio_event WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id.ToString());
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<PortfolioEvent>> ReadAllAsync(SqliteCommand cmd, CancellationToken ct)
    {
        var results = new List<PortfolioEvent>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var kind = Enum.TryParse<PortfolioEventKind>(reader.GetString(2), ignoreCase: true, out var k)
                ? k
                : PortfolioEventKind.UserNote;
            results.Add(new PortfolioEvent(
                Guid.Parse(reader.GetString(0)),
                DateOnly.Parse(reader.GetString(1)),
                kind,
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : (decimal)reader.GetDouble(5),
                reader.IsDBNull(6) ? null : reader.GetString(6)));
        }
        return results;
    }

    private static void Bind(SqliteCommand cmd, PortfolioEvent e)
    {
        cmd.Parameters.AddWithValue("$id", e.Id.ToString());
        cmd.Parameters.AddWithValue("$d", e.Date.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$k", e.Kind.ToString());
        cmd.Parameters.AddWithValue("$l", e.Label);
        cmd.Parameters.AddWithValue("$desc", (object?)e.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$amt", e.Amount.HasValue ? (double)e.Amount.Value : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$sym", (object?)e.Symbol ?? DBNull.Value);
    }
}
