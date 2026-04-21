using Microsoft.Data.Sqlite;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;

namespace Assetra.Infrastructure.Persistence;

public sealed class PortfolioPositionLogSqliteRepository : IPortfolioPositionLogRepository
{
    private readonly string _connectionString;

    public PortfolioPositionLogSqliteRepository(string dbPath)
    {
        _connectionString = $"Data Source={dbPath}";
        PortfolioPositionLogSchemaMigrator.EnsureInitialized(_connectionString);
    }

    public async Task LogAsync(PortfolioPositionLog entry)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync().ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO portfolio_position_log
                (log_id, log_date, position_id, symbol, exchange, quantity, buy_price)
            VALUES ($lid, $ld, $pid, $sym, $exc, $qty, $bp);
            """;
        BindEntry(cmd, entry);
        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    public async Task LogBatchAsync(IEnumerable<PortfolioPositionLog> entries)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync().ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = (SqliteTransaction)tx;
        cmd.CommandText = """
            INSERT OR IGNORE INTO portfolio_position_log
                (log_id, log_date, position_id, symbol, exchange, quantity, buy_price)
            VALUES ($lid, $ld, $pid, $sym, $exc, $qty, $bp);
            """;
        // Add parameters once; rebind values per row
        cmd.Parameters.Add("$lid", SqliteType.Text);
        cmd.Parameters.Add("$ld", SqliteType.Text);
        cmd.Parameters.Add("$pid", SqliteType.Text);
        cmd.Parameters.Add("$sym", SqliteType.Text);
        cmd.Parameters.Add("$exc", SqliteType.Text);
        cmd.Parameters.Add("$qty", SqliteType.Integer);
        cmd.Parameters.Add("$bp", SqliteType.Real);

        foreach (var e in entries)
        {
            cmd.Parameters["$lid"].Value = e.LogId.ToString();
            cmd.Parameters["$ld"].Value = e.LogDate.ToString("yyyy-MM-dd");
            cmd.Parameters["$pid"].Value = e.PositionId.ToString();
            cmd.Parameters["$sym"].Value = e.Symbol;
            cmd.Parameters["$exc"].Value = e.Exchange;
            cmd.Parameters["$qty"].Value = e.Quantity;
            cmd.Parameters["$bp"].Value = (double)e.BuyPrice;
            await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
        await tx.CommitAsync().ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PortfolioPositionLog>> GetAllAsync()
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync().ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT log_id, log_date, position_id, symbol, exchange, quantity, buy_price
            FROM   portfolio_position_log
            ORDER  BY log_date;
            """;
        var results = new List<PortfolioPositionLog>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            results.Add(new PortfolioPositionLog(
                Guid.Parse(reader.GetString(0)),
                DateOnly.Parse(reader.GetString(1)),
                Guid.Parse(reader.GetString(2)),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetInt32(5),
                (decimal)reader.GetDouble(6)));
        return results;
    }

    public async Task<bool> HasAnyAsync()
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync().ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM portfolio_position_log LIMIT 1;";
        var count = (long)(await cmd.ExecuteScalarAsync() ?? 0L);
        return count > 0;
    }

    // helpers

    private static void BindEntry(SqliteCommand cmd, PortfolioPositionLog e)
    {
        cmd.Parameters.AddWithValue("$lid", e.LogId.ToString());
        cmd.Parameters.AddWithValue("$ld", e.LogDate.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$pid", e.PositionId.ToString());
        cmd.Parameters.AddWithValue("$sym", e.Symbol);
        cmd.Parameters.AddWithValue("$exc", e.Exchange);
        cmd.Parameters.AddWithValue("$qty", e.Quantity);
        cmd.Parameters.AddWithValue("$bp", (double)e.BuyPrice);
    }
}
