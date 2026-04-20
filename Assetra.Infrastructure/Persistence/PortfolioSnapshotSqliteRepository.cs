using Microsoft.Data.Sqlite;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;

namespace Assetra.Infrastructure.Persistence;

public sealed class PortfolioSnapshotSqliteRepository : IPortfolioSnapshotRepository
{
    private readonly string _connectionString;

    public PortfolioSnapshotSqliteRepository(string dbPath)
    {
        _connectionString = $"Data Source={dbPath}";
        Initialize();
    }

    private void Initialize()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS portfolio_daily_snapshot (
                snapshot_date  TEXT NOT NULL PRIMARY KEY,
                total_cost     REAL NOT NULL,
                market_value   REAL NOT NULL,
                pnl            REAL NOT NULL,
                position_count INTEGER NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();
    }

    public async Task<IReadOnlyList<PortfolioDailySnapshot>> GetSnapshotsAsync(
        DateOnly? from = null, DateOnly? to = null)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync().ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();

        var clauses = new List<string>();
        if (from.HasValue)
        {
            clauses.Add("snapshot_date >= $from");
            cmd.Parameters.AddWithValue("$from", from.Value.ToString("yyyy-MM-dd"));
        }
        if (to.HasValue)
        {
            clauses.Add("snapshot_date <= $to");
            cmd.Parameters.AddWithValue("$to", to.Value.ToString("yyyy-MM-dd"));
        }

        var where = clauses.Count > 0 ? " WHERE " + string.Join(" AND ", clauses) : "";
        cmd.CommandText = $"SELECT snapshot_date, total_cost, market_value, pnl, position_count FROM portfolio_daily_snapshot{where} ORDER BY snapshot_date;";

        var results = new List<PortfolioDailySnapshot>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            results.Add(new PortfolioDailySnapshot(
                DateOnly.Parse(reader.GetString(0)),
                (decimal)reader.GetDouble(1),
                (decimal)reader.GetDouble(2),
                (decimal)reader.GetDouble(3),
                reader.GetInt32(4)));
        return results;
    }

    public async Task<PortfolioDailySnapshot?> GetSnapshotAsync(DateOnly date)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync().ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT snapshot_date, total_cost, market_value, pnl, position_count FROM portfolio_daily_snapshot WHERE snapshot_date = $d;";
        cmd.Parameters.AddWithValue("$d", date.ToString("yyyy-MM-dd"));

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;
        return new PortfolioDailySnapshot(
            DateOnly.Parse(reader.GetString(0)),
            (decimal)reader.GetDouble(1),
            (decimal)reader.GetDouble(2),
            (decimal)reader.GetDouble(3),
            reader.GetInt32(4));
    }

    public async Task UpsertAsync(PortfolioDailySnapshot snapshot)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync().ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO portfolio_daily_snapshot
                (snapshot_date, total_cost, market_value, pnl, position_count)
            VALUES ($d, $tc, $mv, $pnl, $pc);
            """;
        cmd.Parameters.AddWithValue("$d", snapshot.SnapshotDate.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$tc", (double)snapshot.TotalCost);
        cmd.Parameters.AddWithValue("$mv", (double)snapshot.MarketValue);
        cmd.Parameters.AddWithValue("$pnl", (double)snapshot.Pnl);
        cmd.Parameters.AddWithValue("$pc", snapshot.PositionCount);
        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }
}
