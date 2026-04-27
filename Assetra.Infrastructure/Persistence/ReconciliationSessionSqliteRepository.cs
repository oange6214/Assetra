using System.Text.Json;
using Assetra.Core.Interfaces.Reconciliation;
using Assetra.Core.Models.Import;
using Assetra.Core.Models.Reconciliation;
using Microsoft.Data.Sqlite;

namespace Assetra.Infrastructure.Persistence;

public sealed class ReconciliationSessionSqliteRepository : IReconciliationSessionRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };
    private readonly string _connectionString;

    public ReconciliationSessionSqliteRepository(string dbPath)
    {
        _connectionString = $"Data Source={dbPath};Foreign Keys=True";
        ReconciliationSchemaMigrator.EnsureInitialized(_connectionString);
    }

    public async Task<IReadOnlyList<ReconciliationSession>> GetAllAsync(CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, account_id, period_start, period_end, source_batch_id, created_at, status, note, statement_ending_balance
            FROM reconciliation_session
            ORDER BY created_at DESC;
            """;

        var results = new List<ReconciliationSession>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            results.Add(ReadSession(reader));
        }
        return results;
    }

    public async Task<ReconciliationSession?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, account_id, period_start, period_end, source_batch_id, created_at, status, note, statement_ending_balance
            FROM reconciliation_session
            WHERE id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", id.ToString());
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false) ? ReadSession(reader) : null;
    }

    public async Task AddAsync(
        ReconciliationSession session,
        IReadOnlyList<ImportPreviewRow> statementRows,
        IReadOnlyList<ReconciliationDiff> diffs,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(statementRows);
        ArgumentNullException.ThrowIfNull(diffs);

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct).ConfigureAwait(false);

        await using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO reconciliation_session
                    (id, account_id, period_start, period_end, source_batch_id, created_at, status, note, statement_rows_json, statement_ending_balance)
                VALUES
                    ($id, $account, $ps, $pe, $batch, $created, $status, $note, $rows, $bal);
                """;
            BindSession(cmd, session);
            cmd.Parameters.AddWithValue("$rows", JsonSerializer.Serialize(statementRows, JsonOptions));
            cmd.Parameters.AddWithValue("$bal", session.StatementEndingBalance ?? (object)DBNull.Value);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await InsertDiffsAsync(conn, tx, diffs, ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ImportPreviewRow>> GetStatementRowsAsync(Guid sessionId, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT statement_rows_json FROM reconciliation_session WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", sessionId.ToString());
        var json = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) as string;
        if (string.IsNullOrEmpty(json)) return Array.Empty<ImportPreviewRow>();
        return JsonSerializer.Deserialize<List<ImportPreviewRow>>(json, JsonOptions) ?? new List<ImportPreviewRow>();
    }

    public async Task UpdateStatusAsync(Guid id, ReconciliationStatus status, string? note, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE reconciliation_session
            SET status = $status, note = $note
            WHERE id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", id.ToString());
        cmd.Parameters.AddWithValue("$status", (int)status);
        cmd.Parameters.AddWithValue("$note", note ?? (object)DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task RemoveAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM reconciliation_session WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id.ToString());
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ReconciliationDiff>> GetDiffsAsync(Guid sessionId, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, session_id, kind, statement_row_json, trade_id, resolution, resolved_at, note
            FROM reconciliation_diff
            WHERE session_id = $sid
            ORDER BY kind, id;
            """;
        cmd.Parameters.AddWithValue("$sid", sessionId.ToString());

        var diffs = new List<ReconciliationDiff>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            diffs.Add(ReadDiff(reader));
        }
        return diffs;
    }

    public async Task ReplaceDiffsAsync(
        Guid sessionId,
        IReadOnlyList<ReconciliationDiff> diffs,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(diffs);

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct).ConfigureAwait(false);

        await using (var del = conn.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = "DELETE FROM reconciliation_diff WHERE session_id = $sid;";
            del.Parameters.AddWithValue("$sid", sessionId.ToString());
            await del.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await InsertDiffsAsync(conn, tx, diffs, ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);
    }

    public async Task UpdateDiffResolutionAsync(
        Guid diffId,
        ReconciliationDiffResolution resolution,
        DateTimeOffset? resolvedAt,
        string? note,
        CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE reconciliation_diff
            SET resolution = $r, resolved_at = $at, note = $note
            WHERE id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", diffId.ToString());
        cmd.Parameters.AddWithValue("$r", (int)resolution);
        cmd.Parameters.AddWithValue("$at", resolvedAt?.ToString("o") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$note", note ?? (object)DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task InsertDiffsAsync(
        SqliteConnection conn, SqliteTransaction tx,
        IReadOnlyList<ReconciliationDiff> diffs, CancellationToken ct)
    {
        foreach (var diff in diffs)
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO reconciliation_diff
                    (id, session_id, kind, statement_row_json, trade_id, resolution, resolved_at, note)
                VALUES
                    ($id, $sid, $kind, $row, $tid, $r, $at, $note);
                """;
            cmd.Parameters.AddWithValue("$id", diff.Id.ToString());
            cmd.Parameters.AddWithValue("$sid", diff.SessionId.ToString());
            cmd.Parameters.AddWithValue("$kind", (int)diff.Kind);
            cmd.Parameters.AddWithValue("$row",
                diff.StatementRow is null ? (object)DBNull.Value : JsonSerializer.Serialize(diff.StatementRow, JsonOptions));
            cmd.Parameters.AddWithValue("$tid", diff.TradeId?.ToString() ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$r", (int)diff.Resolution);
            cmd.Parameters.AddWithValue("$at", diff.ResolvedAt?.ToString("o") ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$note", diff.Note ?? (object)DBNull.Value);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    private static void BindSession(SqliteCommand cmd, ReconciliationSession s)
    {
        cmd.Parameters.AddWithValue("$id", s.Id.ToString());
        cmd.Parameters.AddWithValue("$account", s.AccountId.ToString());
        cmd.Parameters.AddWithValue("$ps", s.PeriodStart.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$pe", s.PeriodEnd.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$batch", s.SourceBatchId?.ToString() ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$created", s.CreatedAt.ToString("o"));
        cmd.Parameters.AddWithValue("$status", (int)s.Status);
        cmd.Parameters.AddWithValue("$note", s.Note ?? (object)DBNull.Value);
    }

    private static ReconciliationSession ReadSession(SqliteDataReader reader) => new(
        Id: Guid.Parse(reader.GetString(0)),
        AccountId: Guid.Parse(reader.GetString(1)),
        PeriodStart: DateOnly.Parse(reader.GetString(2)),
        PeriodEnd: DateOnly.Parse(reader.GetString(3)),
        SourceBatchId: reader.IsDBNull(4) ? null : Guid.Parse(reader.GetString(4)),
        CreatedAt: DateTimeOffset.Parse(reader.GetString(5)),
        Status: (ReconciliationStatus)reader.GetInt32(6),
        Note: reader.IsDBNull(7) ? null : reader.GetString(7),
        StatementEndingBalance: reader.IsDBNull(8) ? null : reader.GetDecimal(8));

    private static ReconciliationDiff ReadDiff(SqliteDataReader reader) => new(
        Id: Guid.Parse(reader.GetString(0)),
        SessionId: Guid.Parse(reader.GetString(1)),
        Kind: (ReconciliationDiffKind)reader.GetInt32(2),
        StatementRow: reader.IsDBNull(3)
            ? null
            : JsonSerializer.Deserialize<ImportPreviewRow>(reader.GetString(3), JsonOptions),
        TradeId: reader.IsDBNull(4) ? null : Guid.Parse(reader.GetString(4)),
        Resolution: (ReconciliationDiffResolution)reader.GetInt32(5),
        ResolvedAt: reader.IsDBNull(6) ? null : DateTimeOffset.Parse(reader.GetString(6)),
        Note: reader.IsDBNull(7) ? null : reader.GetString(7));
}
