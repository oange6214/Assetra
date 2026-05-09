using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Microsoft.Data.Sqlite;

namespace Assetra.Infrastructure.Persistence;

/// <summary>
/// Append-only SQLite implementation of <see cref="ITradeAuditRepository"/>.
/// Single INSERT statement; no UPDATE/DELETE methods exposed.
/// </summary>
public sealed class TradeAuditSqliteRepository : ITradeAuditRepository
{
    private readonly string _connectionString;

    public TradeAuditSqliteRepository(string dbPath)
    {
        _connectionString = $"Data Source={dbPath}";
        TradeAuditSchemaMigrator.EnsureInitialized(_connectionString);
    }

    public async Task AppendAsync(TradeAuditEntry entry, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO trade_audit (id, trade_id, action, trade_json, recorded_at, note)
            VALUES ($id, $trade_id, $action, $trade_json, $recorded_at, $note);
            """;
        cmd.Parameters.AddWithValue("$id", entry.Id.ToString());
        cmd.Parameters.AddWithValue("$trade_id", entry.TradeId.ToString());
        cmd.Parameters.AddWithValue("$action", entry.Action);
        cmd.Parameters.AddWithValue("$trade_json", entry.TradeJson);
        cmd.Parameters.AddWithValue("$recorded_at", entry.RecordedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$note", (object?)entry.Note ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TradeAuditEntry>> GetRecentAsync(int limit = 100, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, trade_id, action, trade_json, recorded_at, note
            FROM trade_audit
            ORDER BY recorded_at DESC
            LIMIT $limit;
            """;
        cmd.Parameters.AddWithValue("$limit", limit);
        var results = new List<TradeAuditEntry>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            results.Add(new TradeAuditEntry(
                Id: Guid.Parse(reader.GetString(0)),
                TradeId: Guid.Parse(reader.GetString(1)),
                Action: reader.GetString(2),
                TradeJson: reader.GetString(3),
                RecordedAt: DateTime.Parse(reader.GetString(4), null,
                    System.Globalization.DateTimeStyles.RoundtripKind),
                Note: reader.IsDBNull(5) ? null : reader.GetString(5)));
        }
        return results;
    }
}
