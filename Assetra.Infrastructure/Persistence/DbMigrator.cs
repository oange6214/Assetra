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

    public static async Task<MigrationReport> MigrateAsync(
        string dataDir,
        IPortfolioRepository portfolio,
        IAlertRepository alerts)
    {
        var failures = new List<string>();
        var portfolioImported = await MigratePortfolioAsync(dataDir, portfolio, failures).ConfigureAwait(false);
        var alertsImported = await MigrateAlertsAsync(dataDir, alerts, failures).ConfigureAwait(false);
        return new MigrationReport(portfolioImported, alertsImported, failures);
    }

    private static async Task<int> MigratePortfolioAsync(string dataDir, IPortfolioRepository repo, List<string> failures)
    {
        if ((await repo.GetEntriesAsync()).Count > 0)
            return 0;
        var path = Path.Combine(dataDir, "portfolio.json");
        if (!File.Exists(path))
            return 0;
        try
        {
            var json = await File.ReadAllTextAsync(path);
            var data = JsonSerializer.Deserialize<PortfolioData>(json, JsonOpts);
            if (data?.Entries is null)
                return 0;
            int n = 0;
            foreach (var e in data.Entries)
            {
                await repo.AddAsync(e).ConfigureAwait(false);
                n++;
            }
            return n;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Portfolio JSON migration failed — skipping import from {Path}", path);
            failures.Add($"Portfolio: {ex.Message}");
            return 0;
        }
    }

    private static async Task<int> MigrateAlertsAsync(string dataDir, IAlertRepository repo, List<string> failures)
    {
        if ((await repo.GetRulesAsync()).Count > 0)
            return 0;
        var path = Path.Combine(dataDir, "alerts.json");
        if (!File.Exists(path))
            return 0;
        try
        {
            var json = await File.ReadAllTextAsync(path);
            var data = JsonSerializer.Deserialize<AlertData>(json, JsonOpts);
            if (data?.Rules is null)
                return 0;
            int n = 0;
            foreach (var r in data.Rules)
            {
                await repo.AddAsync(r).ConfigureAwait(false);
                n++;
            }
            return n;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Alerts JSON migration failed — skipping import from {Path}", path);
            failures.Add($"Alerts: {ex.Message}");
            return 0;
        }
    }

    // DTOs matching the JSON file structure from *JsonRepository classes
    private record PortfolioData(List<PortfolioEntry>? Entries);
    private record AlertData(List<AlertRule>? Rules);
}

/// <summary>
/// JSON→SQLite 一次性遷移結果。Failures 為空時代表所有 JSON 來源（若存在）都成功匯入。
/// </summary>
public sealed record MigrationReport(
    int PortfolioImported,
    int AlertsImported,
    IReadOnlyList<string> Failures)
{
    public bool HasFailures => Failures.Count > 0;
    public int TotalImported => PortfolioImported + AlertsImported;
}
