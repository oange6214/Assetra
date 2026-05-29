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
        // P5.17c — startup FX refresh：失敗時 surface snackbar 帶 retry。原本
        // exception 只進 Log，user 沒法知道 rates 退到 hardcoded defaults，
        // 看到 Allocation 顯示舊值會疑惑「為什麼匯率沒生效」(實際是初始 fetch
        // 失敗、用 defaults)。
        _ = Task.Run(async () =>
        {
            var currency = provider.GetRequiredService<ICurrencyService>();
            try
            {
                await currency.RefreshRatesAsync().ConfigureAwait(false);
                // 成功就靜默；user 之後用 Allocation/Trends 自動會看到正確的 rate
                // （CurrencyChanged 已 cascade 到 PortfolioViewModel.OnCurrencyChanged
                // → RebuildTotals → ApplyPositionBaseValuations）。
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Background task {Task} failed at startup", nameof(ICurrencyService.RefreshRatesAsync));
                // 推 snackbar 到 UI thread 帶「重試」action — 一鍵重試直接呼叫
                // RefreshRatesAsync，成功就觸發 CurrencyChanged 整條鏈刷新。
                var snackbar = provider.GetService<ISnackbarService>();
                var localization = provider.GetService<ILocalizationService>();
                if (snackbar is not null)
                {
                    var msg = localization?.Get(
                        "Settings.Fx.StartupFailed",
                        "匯率自動更新失敗，目前使用預設值。") ?? "匯率自動更新失敗，目前使用預設值。";
                    var retryLabel = localization?.Get("Common.Retry", "重試") ?? "重試";
                    System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                        snackbar.Show(msg, retryLabel, () =>
                        {
                            _ = Task.Run(async () =>
                            {
                                try { await currency.RefreshRatesAsync().ConfigureAwait(false); }
                                catch (Exception retryEx)
                                {
                                    Log.Warning(retryEx, "User-triggered FX refresh retry failed");
                                }
                            });
                        }, SnackbarKind.Warning));
                }
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
