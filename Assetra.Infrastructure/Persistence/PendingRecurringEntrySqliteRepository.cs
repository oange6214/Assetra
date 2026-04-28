using System.Globalization;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Microsoft.Data.Sqlite;

namespace Assetra.Infrastructure.Persistence;

public sealed class PendingRecurringEntrySqliteRepository : IPendingRecurringEntryRepository
{
    private readonly string _connectionString;

    public PendingRecurringEntrySqliteRepository(string dbPath)
    {
        _connectionString = $"Data Source={dbPath}";
        RecurringSchemaMigrator.EnsureInitialized(_connectionString);
    }

    private const string SelectClause =
        "id, recurring_source_id, due_date, amount, trade_type, cash_account_id, category_id, " +
        "note, status, generated_trade_id, resolved_at";

    private static PendingRecurringEntry Map(SqliteDataReader r) => new(
        Id: Guid.Parse(r.GetString(0)),
        RecurringSourceId: Guid.Parse(r.GetString(1)),
        DueDate: DateTime.Parse(r.GetString(2), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
        Amount: decimal.Parse(r.GetString(3), CultureInfo.InvariantCulture),
        TradeType: (TradeType)r.GetInt32(4),
        CashAccountId: r.IsDBNull(5) ? null : Guid.Parse(r.GetString(5)),
        CategoryId: r.IsDBNull(6) ? null : Guid.Parse(r.GetString(6)),
        Note: r.IsDBNull(7) ? null : r.GetString(7),
        Status: (PendingStatus)r.GetInt32(8),
        GeneratedTradeId: r.IsDBNull(9) ? null : Guid.Parse(r.GetString(9)),
        ResolvedAt: r.IsDBNull(10) ? null : DateTime.Parse(r.GetString(10), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));

    public async Task<IReadOnlyList<PendingRecurringEntry>> GetAllAsync(CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {SelectClause} FROM pending_recurring_entry ORDER BY due_date DESC;";
        var results = new List<PendingRecurringEntry>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            results.Add(Map(reader));
        return results;
    }

    public async Task<IReadOnlyList<PendingRecurringEntry>> GetByStatusAsync(PendingStatus status, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {SelectClause} FROM pending_recurring_entry WHERE status = $status ORDER BY due_date;";
        cmd.Parameters.AddWithValue("$status", (int)status);
        var results = new List<PendingRecurringEntry>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            results.Add(Map(reader));
        return results;
    }

    public async Task<PendingRecurringEntry?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {SelectClause} FROM pending_recurring_entry WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id.ToString());
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false) ? Map(reader) : null;
    }

    public async Task AddAsync(PendingRecurringEntry entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR IGNORE INTO pending_recurring_entry
                (id, recurring_source_id, due_date, amount, trade_type, cash_account_id, category_id,
                 note, status, generated_trade_id, resolved_at, created_at)
            VALUES
                ($id, $src, $due, $amt, $type, $cash, $cat, $note, $status, $tradeId, $resolved, $now);
            """;
        Bind(cmd, entry);
        cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task UpdateAsync(PendingRecurringEntry entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE pending_recurring_entry SET
                recurring_source_id = $src,
                due_date            = $due,
                amount              = $amt,
                trade_type          = $type,
                cash_account_id     = $cash,
                category_id         = $cat,
                note                = $note,
                status              = $status,
                generated_trade_id  = $tradeId,
                resolved_at         = $resolved
            WHERE id = $id;
            """;
        Bind(cmd, entry);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task RemoveAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM pending_recurring_entry WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id.ToString());
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task RemoveByRecurringSourceAsync(Guid recurringSourceId, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM pending_recurring_entry WHERE recurring_source_id = $src;";
        cmd.Parameters.AddWithValue("$src", recurringSourceId.ToString());
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static void Bind(SqliteCommand cmd, PendingRecurringEntry e)
    {
        cmd.Parameters.AddWithValue("$id", e.Id.ToString());
        cmd.Parameters.AddWithValue("$src", e.RecurringSourceId.ToString());
        cmd.Parameters.AddWithValue("$due", e.DueDate.ToString("o", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$amt", e.Amount.ToString(CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$type", (int)e.TradeType);
        cmd.Parameters.AddWithValue("$cash", (object?)e.CashAccountId?.ToString() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$cat", (object?)e.CategoryId?.ToString() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$note", (object?)e.Note ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$status", (int)e.Status);
        cmd.Parameters.AddWithValue("$tradeId", (object?)e.GeneratedTradeId?.ToString() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$resolved", (object?)e.ResolvedAt?.ToString("o", CultureInfo.InvariantCulture) ?? DBNull.Value);
    }
}
