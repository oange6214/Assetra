using System.Text.Json;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Assetra.Infrastructure.Persistence;

/// <summary>
/// One-time migration: reads existing JSON files and imports into SQLite
/// if the corresponding SQLite table is empty.
/// Safe to call on every startup — does nothing if table already has data.
/// </summary>
public static class DbMigrator
{
    private static ILogger _log = NullLogger.Instance;

    /// <summary>
    /// Optionally supply a logger so migration warnings are visible in the application log.
    /// Call before <see cref="MigrateAsync"/>.
    /// </summary>
    public static void Configure(ILogger logger) =>
        _log = logger ?? NullLogger.Instance;

    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Applies recommended SQLite PRAGMAs to the shared database file.
    /// journal_mode=WAL   — better crash safety; readers don't block writers.
    /// synchronous=NORMAL — safe with WAL; avoids unnecessary fsyncs.
    /// Call once at startup before opening any repositories.
    /// </summary>
    public static async Task ApplyPragmasAsync(string dbPath)
    {
        var cs = $"Data Source={dbPath}";
        await using var conn = new SqliteConnection(cs);
        await conn.OpenAsync().ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;";
        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    public static async Task MigrateAsync(
        string dataDir,
        IPortfolioRepository portfolio,
        IAlertRepository alerts)
    {
        await MigratePortfolioAsync(dataDir, portfolio).ConfigureAwait(false);
        await MigrateAlertsAsync(dataDir, alerts).ConfigureAwait(false);
    }

    private static async Task MigratePortfolioAsync(string dataDir, IPortfolioRepository repo)
    {
        if ((await repo.GetEntriesAsync()).Count > 0)
            return;
        var path = Path.Combine(dataDir, "portfolio.json");
        if (!File.Exists(path))
            return;
        try
        {
            var json = await File.ReadAllTextAsync(path);
            var data = JsonSerializer.Deserialize<PortfolioData>(json, JsonOpts);
            if (data?.Entries is null)
                return;
            foreach (var e in data.Entries)
                await repo.AddAsync(e).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Portfolio JSON migration failed — skipping import from {Path}", path);
        }
    }

    private static async Task MigrateAlertsAsync(string dataDir, IAlertRepository repo)
    {
        if ((await repo.GetRulesAsync()).Count > 0)
            return;
        var path = Path.Combine(dataDir, "alerts.json");
        if (!File.Exists(path))
            return;
        try
        {
            var json = await File.ReadAllTextAsync(path);
            var data = JsonSerializer.Deserialize<AlertData>(json, JsonOpts);
            if (data?.Rules is null)
                return;
            foreach (var r in data.Rules)
                await repo.AddAsync(r).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Alerts JSON migration failed — skipping import from {Path}", path);
        }
    }

    // DTOs matching the JSON file structure from *JsonRepository classes
    private record PortfolioData(List<PortfolioEntry>? Entries);
    private record AlertData(List<AlertRule>? Rules);
}
