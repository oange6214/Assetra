using System.Globalization;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Microsoft.Data.Sqlite;

namespace Assetra.Infrastructure.Persistence;

public sealed class LoanScheduleSqliteRepository : ILoanScheduleRepository
{
    private readonly string _connectionString;

    public LoanScheduleSqliteRepository(string dbPath)
    {
        _connectionString = $"Data Source={dbPath}";
        LoanScheduleSchemaMigrator.EnsureInitialized(_connectionString);
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
        cmd.Parameters.AddWithValue("$id", id.ToString());
        cmd.Parameters.AddWithValue("$pa", paidAt.ToString("o"));
        cmd.Parameters.AddWithValue("$tid", tradeId.ToString());
        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    public async Task ClearPaidByTradeIdAsync(Guid tradeId)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync().ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "UPDATE loan_schedule SET is_paid=0, paid_at=NULL, trade_id=NULL WHERE trade_id=$tid;";
        cmd.Parameters.AddWithValue("$tid", tradeId.ToString());
        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    public async Task ClearPaidWithoutActiveTradeAsync(Guid assetId)
    {
        TradeSchemaMigrator.EnsureInitialized(_connectionString);

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync().ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        // The trade journal is the source of truth for paid schedule rows.
        // Legacy rows may be marked paid without a trade link; those must unlock.
        cmd.CommandText = """
            UPDATE loan_schedule
            SET is_paid=0,
                paid_at=NULL,
                trade_id=NULL
            WHERE asset_id=$aid
              AND is_paid=1
              AND (
                  trade_id IS NULL
                  OR NOT EXISTS (
                      SELECT 1
                      FROM trade
                      WHERE trade.id = loan_schedule.trade_id
                        AND trade.trade_type = 'LoanRepay'
                        AND COALESCE(trade.is_deleted, 0) = 0
                  )
              );
            """;
        cmd.Parameters.AddWithValue("$aid", assetId.ToString());
        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    public async Task ReconcilePaidFromActiveRepaymentsAsync(Guid assetId)
    {
        AssetSchemaMigrator.EnsureInitialized(_connectionString);
        TradeSchemaMigrator.EnsureInitialized(_connectionString);

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync().ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync().ConfigureAwait(false);
        try
        {
            var sqliteTx = (SqliteTransaction)tx;
            var asset = await ReadAssetLoanContextAsync(conn, sqliteTx, assetId).ConfigureAwait(false);
            if (asset is null)
            {
                await tx.CommitAsync().ConfigureAwait(false);
                return;
            }

            var schedules = await ReadScheduleProjectionRowsAsync(conn, sqliteTx, assetId).ConfigureAwait(false);
            if (schedules.Count == 0)
            {
                await tx.CommitAsync().ConfigureAwait(false);
                return;
            }

            var repayments = await ReadRepaymentProjectionsAsync(conn, sqliteTx, assetId, asset.Name).ConfigureAwait(false);
            schedules = await ShiftBorrowDateScheduleIfNeededAsync(
                conn,
                sqliteTx,
                schedules,
                asset.LoanStartDate,
                repayments).ConfigureAwait(false);

            await tx.CommitAsync().ConfigureAwait(false);
        }
        catch
        {
            await tx.RollbackAsync().ConfigureAwait(false);
            throw;
        }
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

    private static async Task<AssetLoanContext?> ReadAssetLoanContextAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        Guid assetId)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            SELECT name, loan_start_date
            FROM asset
            WHERE id=$aid AND COALESCE(is_deleted, 0)=0;
            """;
        cmd.Parameters.AddWithValue("$aid", assetId.ToString());

        await using var r = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
        if (!await r.ReadAsync().ConfigureAwait(false))
            return null;

        var loanStartDate = r.IsDBNull(1)
            ? (DateOnly?)null
            : DateOnly.Parse(r.GetString(1), CultureInfo.InvariantCulture);
        return new AssetLoanContext(r.GetString(0), loanStartDate);
    }

    private static async Task<List<ScheduleProjectionRow>> ReadScheduleProjectionRowsAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        Guid assetId)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            SELECT id, period, due_date
            FROM loan_schedule
            WHERE asset_id=$aid
            ORDER BY period;
            """;
        cmd.Parameters.AddWithValue("$aid", assetId.ToString());

        var rows = new List<ScheduleProjectionRow>();
        await using var r = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
        while (await r.ReadAsync().ConfigureAwait(false))
        {
            rows.Add(new ScheduleProjectionRow(
                Guid.Parse(r.GetString(0)),
                r.GetInt32(1),
                DateOnly.Parse(r.GetString(2), CultureInfo.InvariantCulture)));
        }

        return rows;
    }

    private static async Task<List<RepaymentProjection>> ReadRepaymentProjectionsAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        Guid assetId,
        string loanLabel)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            SELECT id, trade_date
            FROM trade
            WHERE trade_type='LoanRepay'
              AND COALESCE(is_deleted, 0)=0
              AND (
                  liability_asset_id=$aid
                  OR (loan_label IS NOT NULL AND loan_label=$label)
              )
            ORDER BY trade_date, id;
            """;
        cmd.Parameters.AddWithValue("$aid", assetId.ToString());
        cmd.Parameters.AddWithValue("$label", loanLabel);

        var rows = new List<RepaymentProjection>();
        await using var r = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
        while (await r.ReadAsync().ConfigureAwait(false))
        {
            rows.Add(new RepaymentProjection(
                Guid.Parse(r.GetString(0)),
                DateTime.Parse(r.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)));
        }

        return rows;
    }

    private static async Task<List<ScheduleProjectionRow>> ShiftBorrowDateScheduleIfNeededAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        IReadOnlyList<ScheduleProjectionRow> schedules,
        DateOnly? loanStartDate,
        IReadOnlyList<RepaymentProjection> repayments)
    {
        if (loanStartDate is null || schedules.Count == 0 || repayments.Count == 0)
            return schedules.ToList();

        if (schedules[0].DueDate != loanStartDate.Value)
            return schedules.ToList();

        var expectedFirstDue = loanStartDate.Value.AddMonths(1);
        var firstRepaymentDate = DateOnly.FromDateTime(repayments[0].PaidAt.Date);
        if (Math.Abs(firstRepaymentDate.DayNumber - expectedFirstDue.DayNumber) > 3)
            return schedules.ToList();

        var shifted = new List<ScheduleProjectionRow>(schedules.Count);
        foreach (var row in schedules)
        {
            var shiftedDueDate = row.DueDate.AddMonths(1);
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE loan_schedule SET due_date=$due WHERE id=$id;";
            cmd.Parameters.AddWithValue("$id", row.Id.ToString());
            cmd.Parameters.AddWithValue("$due", shiftedDueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            shifted.Add(row with { DueDate = shiftedDueDate });
        }

        return shifted;
    }

    private static LoanScheduleEntry MapEntry(SqliteDataReader r) => new(
        Guid.Parse(r.GetString(0)),
        Guid.Parse(r.GetString(1)),
        r.GetInt32(2),
        DateOnly.Parse(r.GetString(3), CultureInfo.InvariantCulture),
        (decimal)r.GetDouble(4),
        (decimal)r.GetDouble(5),
        (decimal)r.GetDouble(6),
        (decimal)r.GetDouble(7),
        r.GetInt32(8) != 0,
        r.IsDBNull(9) ? null : DateTime.Parse(r.GetString(9), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
        r.IsDBNull(10) ? null : Guid.Parse(r.GetString(10)));

    private static void BindEntry(SqliteCommand cmd, LoanScheduleEntry e)
    {
        cmd.Parameters.AddWithValue("$id", e.Id.ToString());
        cmd.Parameters.AddWithValue("$aid", e.AssetId.ToString());
        cmd.Parameters.AddWithValue("$per", e.Period);
        cmd.Parameters.AddWithValue("$due", e.DueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$tot", (double)e.TotalAmount);
        cmd.Parameters.AddWithValue("$pri", (double)e.PrincipalAmount);
        cmd.Parameters.AddWithValue("$int", (double)e.InterestAmount);
        cmd.Parameters.AddWithValue("$rem", (double)e.Remaining);
        cmd.Parameters.AddWithValue("$ip", e.IsPaid ? 1 : 0);
        cmd.Parameters.AddWithValue("$pa", e.PaidAt.HasValue ? (object)e.PaidAt.Value.ToString("o", CultureInfo.InvariantCulture) : DBNull.Value);
        cmd.Parameters.AddWithValue("$tid", e.TradeId.HasValue ? (object)e.TradeId.Value.ToString() : DBNull.Value);
    }

    private sealed record AssetLoanContext(string Name, DateOnly? LoanStartDate);
    private sealed record ScheduleProjectionRow(Guid Id, int Period, DateOnly DueDate);
    private sealed record RepaymentProjection(Guid Id, DateTime PaidAt);
}
