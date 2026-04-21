using Microsoft.Data.Sqlite;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;

namespace Assetra.Infrastructure.Persistence;

public sealed class LoanScheduleSqliteRepository : ILoanScheduleRepository
{
    private readonly string _connectionString;

    private static readonly HashSet<string> AllowedColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        "is_paid", "paid_at", "trade_id",
    };

    private static readonly HashSet<string> AllowedTypeDefs = new(StringComparer.OrdinalIgnoreCase)
    {
        "INTEGER NOT NULL DEFAULT 0",
        "TEXT",
    };

    public LoanScheduleSqliteRepository(string dbPath)
    {
        _connectionString = $"Data Source={dbPath}";
        Initialize();
    }

    private void Initialize()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var tx = conn.BeginTransaction();
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS loan_schedule (
                    id               TEXT PRIMARY KEY,
                    asset_id         TEXT NOT NULL REFERENCES asset(id),
                    period           INTEGER NOT NULL,
                    due_date         TEXT NOT NULL,
                    total_amount     REAL NOT NULL,
                    principal_amount REAL NOT NULL,
                    interest_amount  REAL NOT NULL,
                    remaining        REAL NOT NULL,
                    is_paid          INTEGER NOT NULL DEFAULT 0,
                    paid_at          TEXT,
                    trade_id         TEXT,
                    UNIQUE (asset_id, period)
                );
                CREATE INDEX IF NOT EXISTS idx_loan_schedule_asset ON loan_schedule (asset_id);
                """;
            cmd.ExecuteNonQuery();

            // Idempotent column additions for future waves
            SqliteSchemaHelper.MigrateAddColumn(conn, tx, "loan_schedule",
                "is_paid", "INTEGER NOT NULL DEFAULT 0", AllowedColumns, AllowedTypeDefs);
            SqliteSchemaHelper.MigrateAddColumn(conn, tx, "loan_schedule",
                "paid_at", "TEXT", AllowedColumns, AllowedTypeDefs);
            SqliteSchemaHelper.MigrateAddColumn(conn, tx, "loan_schedule",
                "trade_id", "TEXT", AllowedColumns, AllowedTypeDefs);

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public async Task<IReadOnlyList<LoanScheduleEntry>> GetByAssetAsync(Guid assetId)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync().ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT id, asset_id, period, due_date, total_amount, principal_amount, " +
            "interest_amount, remaining, is_paid, paid_at, trade_id " +
            "FROM loan_schedule WHERE asset_id=$aid ORDER BY period;";
        cmd.Parameters.AddWithValue("$aid", assetId.ToString());
        var results = new List<LoanScheduleEntry>();
        await using var r = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
        while (await r.ReadAsync().ConfigureAwait(false))
            results.Add(MapEntry(r));
        return results;
    }

    public async Task BulkInsertAsync(IEnumerable<LoanScheduleEntry> entries)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync().ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync().ConfigureAwait(false);
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = (SqliteTransaction)tx;
            cmd.CommandText = """
                INSERT OR IGNORE INTO loan_schedule
                    (id, asset_id, period, due_date, total_amount, principal_amount,
                     interest_amount, remaining, is_paid, paid_at, trade_id)
                VALUES ($id, $aid, $per, $due, $tot, $pri, $int, $rem, $ip, $pa, $tid);
                """;
            foreach (var e in entries)
            {
                cmd.Parameters.Clear();
                BindEntry(cmd, e);
                await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
            await tx.CommitAsync().ConfigureAwait(false);
        }
        catch
        {
            await tx.RollbackAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async Task MarkPaidAsync(Guid id, DateTime paidAt, Guid tradeId)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync().ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "UPDATE loan_schedule SET is_paid=1, paid_at=$pa, trade_id=$tid WHERE id=$id;";
        cmd.Parameters.AddWithValue("$id",  id.ToString());
        cmd.Parameters.AddWithValue("$pa",  paidAt.ToString("o"));
        cmd.Parameters.AddWithValue("$tid", tradeId.ToString());
        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    public async Task DeleteByAssetAsync(Guid assetId)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync().ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM loan_schedule WHERE asset_id=$aid;";
        cmd.Parameters.AddWithValue("$aid", assetId.ToString());
        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static LoanScheduleEntry MapEntry(SqliteDataReader r) => new(
        Guid.Parse(r.GetString(0)),
        Guid.Parse(r.GetString(1)),
        r.GetInt32(2),
        DateOnly.Parse(r.GetString(3)),
        (decimal)r.GetDouble(4),
        (decimal)r.GetDouble(5),
        (decimal)r.GetDouble(6),
        (decimal)r.GetDouble(7),
        r.GetInt32(8) != 0,
        r.IsDBNull(9)  ? null : DateTime.Parse(r.GetString(9)),
        r.IsDBNull(10) ? null : Guid.Parse(r.GetString(10)));

    private static void BindEntry(SqliteCommand cmd, LoanScheduleEntry e)
    {
        cmd.Parameters.AddWithValue("$id",  e.Id.ToString());
        cmd.Parameters.AddWithValue("$aid", e.AssetId.ToString());
        cmd.Parameters.AddWithValue("$per", e.Period);
        cmd.Parameters.AddWithValue("$due", e.DueDate.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$tot", (double)e.TotalAmount);
        cmd.Parameters.AddWithValue("$pri", (double)e.PrincipalAmount);
        cmd.Parameters.AddWithValue("$int", (double)e.InterestAmount);
        cmd.Parameters.AddWithValue("$rem", (double)e.Remaining);
        cmd.Parameters.AddWithValue("$ip",  e.IsPaid ? 1 : 0);
        cmd.Parameters.AddWithValue("$pa",  e.PaidAt.HasValue  ? (object)e.PaidAt.Value.ToString("o") : DBNull.Value);
        cmd.Parameters.AddWithValue("$tid", e.TradeId.HasValue ? (object)e.TradeId.Value.ToString()   : DBNull.Value);
    }
}
