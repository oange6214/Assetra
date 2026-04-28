using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Microsoft.Data.Sqlite;

namespace Assetra.Infrastructure.Persistence;

public sealed class FxRateSqliteRepository : IFxRateRepository
{
    private readonly string _connectionString;

    public FxRateSqliteRepository(string dbPath)
    {
        _connectionString = $"Data Source={dbPath}";
        FxRateSchemaMigrator.EnsureInitialized(_connectionString);
    }

    public async Task UpsertAsync(FxRate rate, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(rate);
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        WriteUpsert(cmd, rate);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task UpsertManyAsync(IReadOnlyList<FxRate> rates, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(rates);
        if (rates.Count == 0) return;

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            foreach (var r in rates)
            {
                ct.ThrowIfCancellationRequested();
                await using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                WriteUpsert(cmd, r);
                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
            await tx.CommitAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            await tx.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<FxRate?> GetAsync(string from, string to, DateOnly asOf, CancellationToken ct = default)
    {
        var (f, t) = (Normalize(from), Normalize(to));
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT from_ccy, to_ccy, as_of_date, rate FROM fx_rate
            WHERE from_ccy = $f AND to_ccy = $t AND as_of_date <= $d
            ORDER BY as_of_date DESC LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$f", f);
        cmd.Parameters.AddWithValue("$t", t);
        cmd.Parameters.AddWithValue("$d", asOf.ToString("yyyy-MM-dd"));
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await r.ReadAsync(ct).ConfigureAwait(false)) return null;
        return new FxRate(r.GetString(0), r.GetString(1),
            (decimal)r.GetDouble(3), DateOnly.Parse(r.GetString(2)));
    }

    public async Task<IReadOnlyList<FxRate>> GetRangeAsync(
        string from, string to, DateOnly start, DateOnly end, CancellationToken ct = default)
    {
        var (f, t) = (Normalize(from), Normalize(to));
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT from_ccy, to_ccy, as_of_date, rate FROM fx_rate
            WHERE from_ccy = $f AND to_ccy = $t AND as_of_date BETWEEN $s AND $e
            ORDER BY as_of_date ASC;
            """;
        cmd.Parameters.AddWithValue("$f", f);
        cmd.Parameters.AddWithValue("$t", t);
        cmd.Parameters.AddWithValue("$s", start.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$e", end.ToString("yyyy-MM-dd"));
        var list = new List<FxRate>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
        {
            list.Add(new FxRate(rd.GetString(0), rd.GetString(1),
                (decimal)rd.GetDouble(3), DateOnly.Parse(rd.GetString(2))));
        }
        return list;
    }

    private static void WriteUpsert(SqliteCommand cmd, FxRate r)
    {
        cmd.CommandText = """
            INSERT INTO fx_rate (from_ccy, to_ccy, as_of_date, rate)
            VALUES ($f, $t, $d, $r)
            ON CONFLICT(from_ccy, to_ccy, as_of_date) DO UPDATE SET rate = excluded.rate;
            """;
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("$f", Normalize(r.From));
        cmd.Parameters.AddWithValue("$t", Normalize(r.To));
        cmd.Parameters.AddWithValue("$d", r.AsOfDate.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$r", (double)r.Rate);
    }

    private static string Normalize(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Currency code is required.", nameof(code));
        return code.Trim().ToUpperInvariant();
    }
}
