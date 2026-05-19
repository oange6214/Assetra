using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Assetra.Infrastructure.Search;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace Assetra.WPF.Infrastructure;

internal static class AppStartupTasks
{
    public static void ApplySavedUiPreferences(IServiceProvider provider, AppSettings savedSettings)
    {
        var themeService = provider.GetRequiredService<IThemeService>();
        ColorSchemeService.Apply(savedSettings.TaiwanColorScheme, themeService.CurrentTheme);

        var localization = provider.GetRequiredService<ILocalizationService>();
        if (!string.IsNullOrWhiteSpace(savedSettings.Language) && savedSettings.Language != "zh-TW")
            localization.SetLanguage(savedSettings.Language);
    }

    public static void StartBackgroundWarmups(IServiceProvider provider, string assetsDir)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await provider.GetRequiredService<ICurrencyService>().RefreshRatesAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Background task {Task} failed at startup", nameof(ICurrencyService.RefreshRatesAsync));
            }
        });

        _ = Task.Run(async () =>
        {
            try
            {
                var updated = await StockListDownloader.UpdateAsync(assetsDir);
                if (updated)
                {
                    var search = provider.GetRequiredService<IStockSearchService>();
                    if (search is StockSearchService svc)
                        svc.Reload(assetsDir);
                }

                provider.GetRequiredService<IStockSearchService>().GetAll();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Background task {Task} failed at startup", nameof(StockListDownloader.UpdateAsync));
            }
        });

        _ = Task.Run(async () =>
        {
            try
            {
                var directory = provider.GetService<IRefreshableSymbolDirectory>();
                if (directory is not null)
                    await directory.RefreshAsync(force: false).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Background task {Task} failed at startup", nameof(IRefreshableSymbolDirectory.RefreshAsync));
            }
        });

        // MultiCurrency-Reporting P4.1c — populate fx_rate_history from Yahoo
        // on every startup. Last 7 days of data is enough to fill weekend /
        // holiday gaps for reports rendered "today". Larger backfills will
        // come from a future settings-page button.
        _ = Task.Run(async () =>
        {
            try
            {
                // Brief delay so we don't race the splash + main window construction
                // for CPU. FX backfill isn't time-sensitive.
                await Task.Delay(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                var refresher = provider.GetService<Assetra.Application.Fx.FxRateHistoryRefresher>();
                if (refresher is null) return;
                var settings = provider.GetService<IAppSettingsService>()?.Current;
                var baseCcy = string.IsNullOrWhiteSpace(settings?.BaseCurrency)
                    ? "TWD" : settings!.BaseCurrency;
                await refresher.RefreshAsync(
                    baseCcy,
                    Assetra.Application.Fx.FxRateHistoryRefresher.DefaultForeignCurrencies,
                    daysBack: 7).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Background task {Task} failed at startup",
                    nameof(Assetra.Application.Fx.FxRateHistoryRefresher));
            }
        });
    }
}
