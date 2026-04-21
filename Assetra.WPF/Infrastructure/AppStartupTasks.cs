using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Assetra.Infrastructure.Search;

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
    }
}
