using System.IO;
using Assetra.Core.Interfaces;
using Assetra.Infrastructure.Persistence;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Assetra.WPF.Infrastructure;

internal sealed class DbInitializerService : IHostedService
{
    private readonly IPortfolioRepository _portfolio;
    private readonly IAlertRepository _alerts;
    private readonly ITradeRepository _trades;
    private readonly ICategoryRepository _categories;
    private readonly IStockraImportService _importService;
    private readonly ISnackbarService _snackbar;
    private readonly ILogger<DbInitializerService> _logger;

    public DbInitializerService(
        IPortfolioRepository portfolio,
        IAlertRepository alerts,
        ITradeRepository trades,
        ICategoryRepository categories,
        IStockraImportService importService,
        ISnackbarService snackbar,
        ILogger<DbInitializerService> logger)
    {
        _portfolio = portfolio;
        _alerts = alerts;
        _trades = trades;
        _categories = categories;
        _importService = importService;
        _snackbar = snackbar;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Assetra");
        var dbPath = Path.Combine(dataDir, "assetra.db");

        // First-run marker: if the DB file doesn't exist yet, we'll trigger the
        // one-time Stockra import *after* the pragmas/migration pass creates it.
        var isFirstRun = !File.Exists(dbPath);

        // ── Step 1: SQLite pragmas + JSON→SQLite data migration ─────────────
        try
        {
            Directory.CreateDirectory(dataDir);
            await DbMigrator.ApplyPragmasAsync(dbPath).ConfigureAwait(false);
            await DbMigrator.MigrateAsync(dataDir, _portfolio, _alerts)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Database migration failed; existing data is not affected");
        }

        // ── Step 2: Migrate legacy Transfer pairs → native TradeType.Transfer ──
        try
        {
            var transferMigration = new TransferPairMigrationService(_trades);
            await transferMigration.MigrateAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Transfer pair migration failed; existing transfer records are not affected");
        }

        // ── Step 3: seed default expense/income categories on first install ──
        try
        {
            await CategorySeeder.EnsureSeededAsync(_categories, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Category seeding failed; existing categories are not affected");
        }

        // ── Step 4 (first-run only): import from Stockra DB if present ──────
        if (isFirstRun)
        {
            await TryImportFromStockraAsync().ConfigureAwait(false);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task TryImportFromStockraAsync()
    {
        // Source: %APPDATA%/Stockra/stockra.db — intentionally unchanged product name;
        // this is the legacy Stockra installation we're migrating FROM.
        var stockraDb = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Stockra", "stockra.db");

        if (!File.Exists(stockraDb))
            return;

        try
        {
            var result = await _importService.ImportAsync(stockraDb).ConfigureAwait(false);
            if (result.TotalRows > 0)
            {
                _snackbar.Show($"已從 Stockra 匯入 {result.TotalRows} 筆資料", SnackbarKind.Info);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Stockra DB import failed");
            _snackbar.Warning("Stockra 資料匯入失敗，請至設定手動重試");
        }
    }
}
