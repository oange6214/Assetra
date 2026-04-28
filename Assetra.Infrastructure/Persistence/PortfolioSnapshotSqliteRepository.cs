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
        PortfolioSnapshotSchemaMigrator.EnsureInitialized(_connectionString);
    }

    private const string SelectColumns = "snapshot_date, total_cost, market_value, pnl, position_count, currency";

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
        cmd.CommandText = $"SELECT {SelectColumns} FROM portfolio_daily_snapshot{where} ORDER BY snapshot_date;";

        var results = new List<PortfolioDailySnapshot>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            results.Add(Read(reader));
        return results;
    }

    public async Task<PortfolioDailySnapshot?> GetSnapshotAsync(DateOnly date)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync().ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {SelectColumns} FROM portfolio_daily_snapshot WHERE snapshot_date = $d;";
        cmd.Parameters.AddWithValue("$d", date.ToString("yyyy-MM-dd"));

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;
        return Read(reader);
    }

    public async Task UpsertAsync(PortfolioDailySnapshot snapshot)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync().ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO portfolio_daily_snapshot
                (snapshot_date, total_cost, market_value, pnl, position_count, currency)
            VALUES ($d, $tc, $mv, $pnl, $pc, $ccy);
            """;
        cmd.Parameters.AddWithValue("$d", snapshot.SnapshotDate.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$tc", (double)snapshot.TotalCost);
        cmd.Parameters.AddWithValue("$mv", (double)snapshot.MarketValue);
        cmd.Parameters.AddWithValue("$pnl", (double)snapshot.Pnl);
        cmd.Parameters.AddWithValue("$pc", snapshot.PositionCount);
        cmd.Parameters.AddWithValue("$ccy", string.IsNullOrWhiteSpace(snapshot.Currency) ? "TWD" : snapshot.Currency);
        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static PortfolioDailySnapshot Read(SqliteDataReader reader) =>
        new(
            DateOnly.Parse(reader.GetString(0)),
            (decimal)reader.GetDouble(1),
            (decimal)reader.GetDouble(2),
            (decimal)reader.GetDouble(3),
            reader.GetInt32(4),
            reader.IsDBNull(5) ? "TWD" : reader.GetString(5));
}
