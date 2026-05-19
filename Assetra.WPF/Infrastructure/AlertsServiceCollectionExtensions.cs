using System.Reactive.Concurrency;
using Assetra.Application.Alerts.Contracts;
using Assetra.Application.Alerts.Services;
using Assetra.Core.Interfaces;
using Assetra.Core.Interfaces.Sync;
using Assetra.Infrastructure.Persistence;
using Assetra.WPF.Features.Alerts;
using Assetra.WPF.Features.Snackbar;
using Microsoft.Extensions.DependencyInjection;

namespace Assetra.WPF.Infrastructure;

internal static class AlertsServiceCollectionExtensions
{
    public static IServiceCollection AddAlertsContext(
        this IServiceCollection services,
        string dbPath)
    {
        // Sync-Status-Indicator 補洞：AlertSqliteRepository 一個物件同時實作 repo + sync store
        // (跟 RetirementAccount / RealEstate pattern 對齊)。需要 deviceId 才能 stamp version。
        services.AddSingleton<AlertSqliteRepository>(sp =>
        {
            var settings = sp.GetRequiredService<IAppSettingsService>();
            return new AlertSqliteRepository(dbPath, () => SyncDeviceIdProvider.Resolve(settings));
        });
        services.AddSingleton<IAlertRepository>(sp => sp.GetRequiredService<AlertSqliteRepository>());
        services.AddSingleton<IAlertSyncStore>(sp => sp.GetRequiredService<AlertSqliteRepository>());
        services.AddSingleton<IAlertService, AlertService>();

        services.AddSingleton<AlertsViewModel>(sp => new AlertsViewModel(
            sp.GetRequiredService<IAlertService>(),
            sp.GetRequiredService<IStockSearchService>(),
            sp.GetRequiredService<IStockService>(),
            sp.GetRequiredService<IScheduler>(),
            sp.GetRequiredService<ISnackbarService>(),
            sp.GetRequiredService<ILocalizationService>(),
            sp.GetService<ICurrencyService>(),
            sp.GetRequiredService<ISymbolDirectory>()));

        return services;
    }
}
