using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Microsoft.Data.Sqlite;

namespace Assetra.Infrastructure.Persistence;

/// <summary>
/// SQLite-backed <see cref="IFxRateHistoryRepository"/>. Currency codes are
/// stored upper-cased so lookups are deterministic.
/// </summary>
public sealed class FxRateHistorySqliteRepository : IFxRateHistoryRepository
{
    private readonly string _connectionString;

    public FxRateHistorySqliteRepository(string dbPath)
    {
        _connectionString = $"Data Source={dbPath}";
        FxRateHistorySchemaMigrator.EnsureInitialized(_connectionString);
    }

    public async Task<FxRateHistoryEntry?> GetAsync(
        DateOnly date, string baseCcy, string quoteCcy, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(baseCcy) || string.IsNullOrWhiteSpace(quoteCcy))
            return null;
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT date, base_ccy, quote_ccy, rate, source, ingested_at
            FROM fx_rate_history
            WHERE date = $d AND base_ccy = $b AND quote_ccy = $q
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$d", date.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$b", baseCcy.ToUpperInvariant());
        cmd.Parameters.AddWithValue("$q", quoteCcy.ToUpperInvariant());

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false) ? Map(reader) : null;
    }

    public async Task<FxRateHistoryEntry?> GetNearestAsync(
        DateOnly date, string baseCcy, string quoteCcy, int lookbackDays = 7, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(baseCcy) || string.IsNullOrWhiteSpace(quoteCcy))
            return null;
        var floor = date.AddDays(-Math.Max(0, lookbackDays));
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        // Walk backwards along the (base, quote, date) index. ORDER BY date DESC
        // hits the rightmost qualifying row first.
        cmd.CommandText = """
            SELECT date, base_ccy, quote_ccy, rate, source, ingested_at
            FROM fx_rate_history
            WHERE base_ccy = $b AND quote_ccy = $q
              AND date <= $d AND date >= $floor
            ORDER BY date DESC
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$d", date.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$floor", floor.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$b", baseCcy.ToUpperInvariant());
        cmd.Parameters.AddWithValue("$q", quoteCcy.ToUpperInvariant());

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false) ? Map(reader) : null;
    }

    public async Task UpsertRangeAsync(
        IReadOnlyCollection<FxRateHistoryEntry> entries, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Count == 0)
            return;

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO fx_rate_history (date, base_ccy, quote_ccy, rate, source, ingested_at)
            VALUES ($d, $b, $q, $r, $src, $ing)
            ON CONFLICT(date, base_ccy, quote_ccy) DO UPDATE SET
                rate = excluded.rate,
                source = excluded.source,
                ingested_at = excluded.ingested_at;
            """;
        var pd = cmd.Parameters.Add("$d", SqliteType.Text);
        var pb = cmd.Parameters.Add("$b", SqliteType.Text);
        var pq = cmd.Parameters.Add("$q", SqliteType.Text);
        var pr = cmd.Parameters.Add("$r", SqliteType.Real);
        var psrc = cmd.Parameters.Add("$src", SqliteType.Text);
        var ping = cmd.Parameters.Add("$ing", SqliteType.Text);

        foreach (var e in entries)
        {
            pd.Value = e.Date.ToString("yyyy-MM-dd");
            pb.Value = e.BaseCurrency.ToUpperInvariant();
            pq.Value = e.QuoteCurrency.ToUpperInvariant();
            pr.Value = (double)e.Rate;
            psrc.Value = string.IsNullOrWhiteSpace(e.Source) ? "manual" : e.Source;
            ping.Value = e.IngestedAt.UtcDateTime.ToString("o");
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        await tx.CommitAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<FxRateHistoryEntry>> GetRangeAsync(
        string baseCcy, string quoteCcy, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(baseCcy) || string.IsNullOrWhiteSpace(quoteCcy))
            return Array.Empty<FxRateHistoryEntry>();
        if (from > to)
            (from, to) = (to, from);
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT date, base_ccy, quote_ccy, rate, source, ingested_at
            FROM fx_rate_history
            WHERE base_ccy = $b AND quote_ccy = $q
              AND date BETWEEN $from AND $to
            ORDER BY date ASC;
            """;
        cmd.Parameters.AddWithValue("$b", baseCcy.ToUpperInvariant());
        cmd.Parameters.AddWithValue("$q", quoteCcy.ToUpperInvariant());
        cmd.Parameters.AddWithValue("$from", from.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$to", to.ToString("yyyy-MM-dd"));

        var results = new List<FxRateHistoryEntry>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            results.Add(Map(reader));
        return results;
    }

    private static FxRateHistoryEntry Map(SqliteDataReader r) => new(
        Date: DateOnly.Parse(r.GetString(0)),
        BaseCurrency: r.GetString(1),
        QuoteCurrency: r.GetString(2),
        Rate: (decimal)r.GetDouble(3),
        Source: r.GetString(4),
        IngestedAt: DateTimeOffset.Parse(r.GetString(5)));
}
