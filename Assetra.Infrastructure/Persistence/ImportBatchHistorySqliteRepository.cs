using Assetra.Core.Interfaces.Import;
using Assetra.Core.Models.Import;
using Microsoft.Data.Sqlite;

namespace Assetra.Infrastructure.Persistence;

public sealed class ImportBatchHistorySqliteRepository : IImportBatchHistoryRepository
{
    private readonly string _connectionString;

    public ImportBatchHistorySqliteRepository(string dbPath)
    {
        _connectionString = $"Data Source={dbPath};Foreign Keys=True";
        ImportBatchHistorySchemaMigrator.EnsureInitialized(_connectionString);
    }

    public async Task SaveAsync(ImportBatchHistory history, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(history);

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct).ConfigureAwait(false);

        await using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO import_batch_history
                    (id, batch_id, file_name, format, applied_at,
                     rows_applied, rows_skipped, rows_overwritten,
                     is_rolled_back, rolled_back_at)
                VALUES
                    ($id, $batch_id, $file_name, $format, $applied_at,
                     $rows_applied, $rows_skipped, $rows_overwritten,
                     $is_rolled_back, $rolled_back_at);
                """;
            cmd.Parameters.AddWithValue("$id", history.Id.ToString());
            cmd.Parameters.AddWithValue("$batch_id", history.BatchId.ToString());
            cmd.Parameters.AddWithValue("$file_name", history.FileName);
            cmd.Parameters.AddWithValue("$format", (int)history.Format);
            cmd.Parameters.AddWithValue("$applied_at", history.AppliedAt.ToString("o"));
            cmd.Parameters.AddWithValue("$rows_applied", history.RowsApplied);
            cmd.Parameters.AddWithValue("$rows_skipped", history.RowsSkipped);
            cmd.Parameters.AddWithValue("$rows_overwritten", history.RowsOverwritten);
            cmd.Parameters.AddWithValue("$is_rolled_back", history.IsRolledBack ? 1 : 0);
            cmd.Parameters.AddWithValue("$rolled_back_at",
                history.RolledBackAt?.ToString("o") ?? (object)DBNull.Value);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        foreach (var entry in history.Entries)
        {
            ct.ThrowIfCancellationRequested();
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO import_batch_entry
                    (id, history_id, row_index, action, new_trade_id, overwritten_trade_json, preview_row_json)
                VALUES
                    ($id, $history_id, $row_index, $action, $new_trade_id, $json, $row_json);
                """;
            cmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
            cmd.Parameters.AddWithValue("$history_id", history.Id.ToString());
            cmd.Parameters.AddWithValue("$row_index", entry.RowIndex);
            cmd.Parameters.AddWithValue("$action", (int)entry.Action);
            cmd.Parameters.AddWithValue("$new_trade_id",
                entry.NewTradeId?.ToString() ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$json",
                entry.OverwrittenTradeJson ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$row_json",
                entry.PreviewRowJson ?? (object)DBNull.Value);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await tx.CommitAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ImportBatchHistory>> GetRecentAsync(int limit, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, batch_id, file_name, format, applied_at,
                   rows_applied, rows_skipped, rows_overwritten,
                   is_rolled_back, rolled_back_at
            FROM import_batch_history
            ORDER BY applied_at DESC
            LIMIT $limit;
            """;
        cmd.Parameters.AddWithValue("$limit", limit);

        var results = new List<ImportBatchHistory>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            results.Add(ReadHeader(reader, Array.Empty<ImportBatchEntry>()));
        }
        return results;
    }

    public async Task<ImportBatchHistory?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);

        ImportBatchHistory? header = null;

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT id, batch_id, file_name, format, applied_at,
                       rows_applied, rows_skipped, rows_overwritten,
                       is_rolled_back, rolled_back_at
                FROM import_batch_history
                WHERE id = $id;
                """;
            cmd.Parameters.AddWithValue("$id", id.ToString());
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                header = ReadHeader(reader, Array.Empty<ImportBatchEntry>());
            }
        }

        if (header is null) return null;

        var entries = new List<ImportBatchEntry>();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT row_index, action, new_trade_id, overwritten_trade_json, preview_row_json
                FROM import_batch_entry
                WHERE history_id = $hid
                ORDER BY row_index;
                """;
            cmd.Parameters.AddWithValue("$hid", id.ToString());
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                entries.Add(new ImportBatchEntry(
                    RowIndex: reader.GetInt32(0),
                    Action: (ImportBatchAction)reader.GetInt32(1),
                    NewTradeId: reader.IsDBNull(2) ? null : Guid.Parse(reader.GetString(2)),
                    OverwrittenTradeJson: reader.IsDBNull(3) ? null : reader.GetString(3),
                    PreviewRowJson: reader.IsDBNull(4) ? null : reader.GetString(4)));
            }
        }

        return header with { Entries = entries };
    }

    public async Task<IReadOnlyList<ImportPreviewRow>> GetPreviewRowsAsync(Guid historyId, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT preview_row_json
            FROM import_batch_entry
            WHERE history_id = $hid AND preview_row_json IS NOT NULL
            ORDER BY row_index;
            """;
        cmd.Parameters.AddWithValue("$hid", historyId.ToString());

        var rows = new List<ImportPreviewRow>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var json = reader.GetString(0);
            var row = System.Text.Json.JsonSerializer.Deserialize<ImportPreviewRow>(json);
            if (row is not null) rows.Add(row);
        }
        return rows;
    }

    public async Task MarkRolledBackAsync(Guid id, DateTimeOffset rolledBackAt, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE import_batch_history
            SET is_rolled_back = 1, rolled_back_at = $at
            WHERE id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", id.ToString());
        cmd.Parameters.AddWithValue("$at", rolledBackAt.ToString("o"));
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static ImportBatchHistory ReadHeader(SqliteDataReader reader, IReadOnlyList<ImportBatchEntry> entries)
    {
        return new ImportBatchHistory(
            Id: Guid.Parse(reader.GetString(0)),
            BatchId: Guid.Parse(reader.GetString(1)),
            FileName: reader.GetString(2),
            Format: (ImportFormat)reader.GetInt32(3),
            AppliedAt: DateTimeOffset.Parse(reader.GetString(4)),
            RowsApplied: reader.GetInt32(5),
            RowsSkipped: reader.GetInt32(6),
            RowsOverwritten: reader.GetInt32(7),
            IsRolledBack: reader.GetInt32(8) != 0,
            RolledBackAt: reader.IsDBNull(9) ? null : DateTimeOffset.Parse(reader.GetString(9)),
            Entries: entries);
    }
}
