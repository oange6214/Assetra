using Microsoft.Data.Sqlite;
using Assetra.Core.Interfaces;

namespace Assetra.Infrastructure.Persistence;

/// <summary>
/// SQLite-backed implementation of <see cref="IAssistantHistoryRepository"/>.
/// Schema is created on first use (idempotent).
/// </summary>
public sealed class AssistantHistorySqliteRepository : IAssistantHistoryRepository
{
    private readonly string _connectionString;

    public AssistantHistorySqliteRepository(string dbPath)
    {
        _connectionString = $"Data Source={dbPath}";
        EnsureInitialized(_connectionString);
    }

    private static void EnsureInitialized(string connectionString)
    {
        using var conn = new SqliteConnection(connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS assistant_history (
                id TEXT PRIMARY KEY,
                asked_at_utc TEXT NOT NULL,
                user_text TEXT NOT NULL,
                assistant_text TEXT NOT NULL,
                source TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_assistant_history_asked_at ON assistant_history(asked_at_utc DESC);
        ";
        cmd.ExecuteNonQuery();
    }

    public async Task<IReadOnlyList<AssistantHistoryEntry>> GetRecentAsync(int limit = 50, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, asked_at_utc, user_text, assistant_text, source
            FROM assistant_history
            ORDER BY asked_at_utc DESC
            LIMIT $limit;
        ";
        cmd.Parameters.AddWithValue("$limit", limit);
        var results = new List<AssistantHistoryEntry>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            results.Add(new AssistantHistoryEntry(
                Id: Guid.Parse(reader.GetString(0)),
                AskedAt: DateTime.Parse(reader.GetString(1), null,
                    System.Globalization.DateTimeStyles.RoundtripKind),
                UserText: reader.GetString(2),
                AssistantText: reader.GetString(3),
                Source: reader.GetString(4)));
        }
        return results;
    }

    public async Task AddAsync(AssistantHistoryEntry entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO assistant_history (id, asked_at_utc, user_text, assistant_text, source)
            VALUES ($id, $askedAt, $user, $assistant, $source);
        ";
        cmd.Parameters.AddWithValue("$id", entry.Id.ToString());
        cmd.Parameters.AddWithValue("$askedAt", entry.AskedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$user", entry.UserText ?? string.Empty);
        cmd.Parameters.AddWithValue("$assistant", entry.AssistantText ?? string.Empty);
        cmd.Parameters.AddWithValue("$source", entry.Source ?? string.Empty);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task ClearAsync(CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM assistant_history;";
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}
