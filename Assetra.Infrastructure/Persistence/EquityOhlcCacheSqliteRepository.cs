using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Microsoft.Data.Sqlite;

namespace Assetra.Infrastructure.Persistence;

public sealed class EquityOhlcCacheSqliteRepository : IEquityOhlcCacheRepository
{
    private readonly string _connectionString;

    public EquityOhlcCacheSqliteRepository(string dbPath)
    {
        _connectionString = $"Data Source={dbPath}";
        EquityOhlcCacheSchemaMigrator.EnsureInitialized(_connectionString);
    }

    public async Task UpsertManyAsync(IReadOnlyList<EquityOhlcCacheEntry> candles, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(candles);
        if (candles.Count == 0)
            return;

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            foreach (var candle in candles)
            {
                ct.ThrowIfCancellationRequested();
                await using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                WriteUpsert(cmd, candle);
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

    public async Task<IReadOnlyList<EquityOhlcCacheEntry>> GetRangeAsync(
        string symbol,
        string exchange,
        string interval,
        DateOnly start,
        DateOnly end,
        CancellationToken ct = default)
    {
        if (end < start)
            return [];

        var key = new EquityInstrumentKey(symbol, exchange);
        var normalizedInterval = NormalizeInterval(interval);

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT symbol, exchange, interval, trade_date, open, high, low, close,
                   volume, currency, source_provider, source_updated_at, is_adjusted
            FROM equity_ohlc_cache
            WHERE symbol = $symbol
              AND exchange = $exchange
              AND interval = $interval
              AND trade_date BETWEEN $start AND $end
            ORDER BY trade_date ASC;
            """;
        cmd.Parameters.AddWithValue("$symbol", key.Symbol);
        cmd.Parameters.AddWithValue("$exchange", key.Exchange);
        cmd.Parameters.AddWithValue("$interval", normalizedInterval);
        cmd.Parameters.AddWithValue("$start", start.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$end", end.ToString("yyyy-MM-dd"));

        var rows = new List<EquityOhlcCacheEntry>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(ReadEntry(reader));
        }

        return rows;
    }

    private static void WriteUpsert(SqliteCommand cmd, EquityOhlcCacheEntry entry)
    {
        var key = entry.InstrumentKey;
        var interval = NormalizeInterval(entry.Interval);
        var currency = NormalizeCurrency(entry.Currency, key.Exchange);
        var provider = string.IsNullOrWhiteSpace(entry.SourceProvider)
            ? "unknown"
            : entry.SourceProvider.Trim();

        cmd.CommandText = """
            INSERT INTO equity_ohlc_cache
                (symbol, exchange, interval, trade_date, open, high, low, close,
                 volume, currency, source_provider, source_updated_at, is_adjusted)
            VALUES
                ($symbol, $exchange, $interval, $trade_date, $open, $high, $low, $close,
                 $volume, $currency, $source_provider, $source_updated_at, $is_adjusted)
            ON CONFLICT(symbol, exchange, interval, trade_date) DO UPDATE SET
                open = excluded.open,
                high = excluded.high,
                low = excluded.low,
                close = excluded.close,
                volume = excluded.volume,
                currency = excluded.currency,
                source_provider = excluded.source_provider,
                source_updated_at = excluded.source_updated_at,
                is_adjusted = excluded.is_adjusted;
            """;
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("$symbol", key.Symbol);
        cmd.Parameters.AddWithValue("$exchange", key.Exchange);
        cmd.Parameters.AddWithValue("$interval", interval);
        cmd.Parameters.AddWithValue("$trade_date", entry.Candle.Date.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$open", (double)entry.Candle.Open);
        cmd.Parameters.AddWithValue("$high", (double)entry.Candle.High);
        cmd.Parameters.AddWithValue("$low", (double)entry.Candle.Low);
        cmd.Parameters.AddWithValue("$close", (double)entry.Candle.Close);
        cmd.Parameters.AddWithValue("$volume", entry.Candle.Volume);
        cmd.Parameters.AddWithValue("$currency", currency);
        cmd.Parameters.AddWithValue("$source_provider", provider);
        cmd.Parameters.AddWithValue("$source_updated_at", entry.SourceUpdatedAt.ToUniversalTime().ToString("O"));
        cmd.Parameters.AddWithValue("$is_adjusted", entry.IsAdjusted ? 1 : 0);
    }

    private static EquityOhlcCacheEntry ReadEntry(SqliteDataReader reader)
    {
        var candle = new OhlcvPoint(
            DateOnly.Parse(reader.GetString(3)),
            (decimal)reader.GetDouble(4),
            (decimal)reader.GetDouble(5),
            (decimal)reader.GetDouble(6),
            (decimal)reader.GetDouble(7),
            reader.GetInt64(8));

        return new EquityOhlcCacheEntry(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            candle,
            reader.GetString(9),
            reader.GetString(10),
            DateTimeOffset.Parse(reader.GetString(11)),
            reader.GetInt32(12) != 0);
    }

    private static string NormalizeInterval(string interval) =>
        string.IsNullOrWhiteSpace(interval) ? "1d" : interval.Trim().ToLowerInvariant();

    private static string NormalizeCurrency(string currency, string exchange)
    {
        if (!string.IsNullOrWhiteSpace(currency))
            return currency.Trim().ToUpperInvariant();

        return StockExchangeRegistry.ResolveDefaultCurrency(exchange);
    }
}
