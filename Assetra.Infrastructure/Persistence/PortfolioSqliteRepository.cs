using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Microsoft.Data.Sqlite;

namespace Assetra.Infrastructure.Persistence;

public sealed class PortfolioSqliteRepository : IPortfolioRepository
{
    private readonly string _connectionString;

    public PortfolioSqliteRepository(string dbPath)
    {
        _connectionString = $"Data Source={dbPath}";
        PortfolioSchemaMigrator.EnsureInitialized(_connectionString);
    }

    public async Task<IReadOnlyList<PortfolioEntry>> GetEntriesAsync(CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, symbol, exchange, asset_type, display_name, currency, is_active, is_etf FROM portfolio ORDER BY rowid;";
        var results = new List<PortfolioEntry>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var assetType = Enum.TryParse<AssetType>(reader.GetString(3), out var t) ? t : AssetType.Stock;
            var isActive = reader.IsDBNull(6) ? true : reader.GetInt64(6) != 0;
            var isEtf = !reader.IsDBNull(7) && reader.GetInt64(7) != 0;
            results.Add(new PortfolioEntry(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1),
                reader.GetString(2),
                assetType,
                reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                reader.IsDBNull(5) ? "TWD" : reader.GetString(5),
                isActive,
                isEtf));
        }
        return results;
    }

    public async Task AddAsync(PortfolioEntry entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR IGNORE INTO portfolio (id, symbol, exchange, asset_type, display_name, currency, created_at, updated_at, is_active, is_etf)
            VALUES ($id, $sym, $ex, $at, $dn, $cur, $created_at, $updated_at, $ia, $etf);
            """;
        cmd.Parameters.AddWithValue("$id", entry.Id.ToString());
        cmd.Parameters.AddWithValue("$sym", entry.Symbol);
        cmd.Parameters.AddWithValue("$ex", entry.Exchange);
        cmd.Parameters.AddWithValue("$at", entry.AssetType.ToString());
        cmd.Parameters.AddWithValue("$dn", entry.DisplayName);
        cmd.Parameters.AddWithValue("$cur", entry.Currency);
        var now = DateTime.UtcNow.ToString("o");
        cmd.Parameters.AddWithValue("$created_at", now);
        cmd.Parameters.AddWithValue("$updated_at", now);
        cmd.Parameters.AddWithValue("$ia", entry.IsActive ? 1 : 0);
        cmd.Parameters.AddWithValue("$etf", entry.IsEtf ? 1 : 0);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task UpdateAsync(PortfolioEntry entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE portfolio SET asset_type=$at, updated_at=$updated_at
            WHERE id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", entry.Id.ToString());
        cmd.Parameters.AddWithValue("$at", entry.AssetType.ToString());
        cmd.Parameters.AddWithValue("$updated_at", DateTime.UtcNow.ToString("o"));
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task UpdateMetadataAsync(Guid id, string displayName, string currency, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE portfolio SET display_name=$dn, currency=$cur, updated_at=$updated_at
            WHERE id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", id.ToString());
        cmd.Parameters.AddWithValue("$dn", displayName);
        cmd.Parameters.AddWithValue("$cur", currency);
        cmd.Parameters.AddWithValue("$updated_at", DateTime.UtcNow.ToString("o"));
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task RemoveAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM portfolio WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id.ToString());
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PortfolioEntry>> GetActiveAsync(CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, symbol, exchange, asset_type, display_name, currency, is_active, is_etf FROM portfolio WHERE is_active = 1 ORDER BY rowid;";
        var results = new List<PortfolioEntry>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var assetType = Enum.TryParse<AssetType>(reader.GetString(3), out var t) ? t : AssetType.Stock;
            var isEtf = !reader.IsDBNull(7) && reader.GetInt64(7) != 0;
            results.Add(new PortfolioEntry(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1),
                reader.GetString(2),
                assetType,
                reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                reader.IsDBNull(5) ? "TWD" : reader.GetString(5),
                IsActive: true,
                IsEtf: isEtf));
        }
        return results;
    }

    public async Task<Guid> FindOrCreatePortfolioEntryAsync(
        string symbol, string exchange, string? displayName, AssetType assetType,
        string? currency = null,
        bool isEtf = false,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        ArgumentException.ThrowIfNullOrWhiteSpace(exchange);
        var resolvedCurrency = string.IsNullOrWhiteSpace(currency) ? "TWD" : currency.Trim().ToUpperInvariant();

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);

        // Existing?
        await using (var sel = conn.CreateCommand())
        {
            sel.CommandText = "SELECT id FROM portfolio WHERE symbol = $s AND exchange = $e LIMIT 1";
            sel.Parameters.AddWithValue("$s", symbol);
            sel.Parameters.AddWithValue("$e", exchange);
            var r = await sel.ExecuteScalarAsync(ct).ConfigureAwait(false);
            if (r is string s1)
                return Guid.Parse(s1);
        }

        var id = Guid.NewGuid();
        try
        {
            await using var ins = conn.CreateCommand();
            ins.CommandText = @"INSERT INTO portfolio(id, symbol, exchange, asset_type, created_at, updated_at, display_name, currency, is_active, is_etf)
                                VALUES($id, $s, $e, $t, datetime('now'), datetime('now'), $dn, $cur, 1, $etf)";
            ins.Parameters.AddWithValue("$id", id.ToString());
            ins.Parameters.AddWithValue("$s", symbol);
            ins.Parameters.AddWithValue("$e", exchange);
            ins.Parameters.AddWithValue("$t", assetType.ToString());
            ins.Parameters.AddWithValue("$dn", (object?)displayName ?? "");
            ins.Parameters.AddWithValue("$cur", resolvedCurrency);
            ins.Parameters.AddWithValue("$etf", isEtf ? 1 : 0);
            await ins.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return id;
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19 /* SQLITE_CONSTRAINT */)
        {
            await using var sel2 = conn.CreateCommand();
            sel2.CommandText = "SELECT id FROM portfolio WHERE symbol = $s AND exchange = $e LIMIT 1";
            sel2.Parameters.AddWithValue("$s", symbol);
            sel2.Parameters.AddWithValue("$e", exchange);
            var r2 = await sel2.ExecuteScalarAsync(ct).ConfigureAwait(false);
            if (r2 is string s2)
                return Guid.Parse(s2);
            throw;
        }
    }

    public async Task ArchiveAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE portfolio SET is_active = 0, updated_at = datetime('now') WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id.ToString());
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task UnarchiveAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE portfolio SET is_active = 1, updated_at = datetime('now') WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id.ToString());
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<int> HasTradeReferencesAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var checkCmd = conn.CreateCommand();
        checkCmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='trade'";
        var tableExists = await checkCmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        if (tableExists is null or DBNull || Convert.ToInt32(tableExists, System.Globalization.CultureInfo.InvariantCulture) == 0)
            return 0;
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM trade WHERE portfolio_entry_id = $id";
        cmd.Parameters.AddWithValue("$id", id.ToString());
        var r = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return r is null or DBNull ? 0 : Convert.ToInt32(r, System.Globalization.CultureInfo.InvariantCulture);
    }
}
