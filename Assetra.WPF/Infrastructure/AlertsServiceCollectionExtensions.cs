using System.Reactive.Concurrency;
using Assetra.Application.Alerts.Contracts;
using Assetra.Application.Alerts.Services;
using Assetra.Core.Interfaces;
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
        services.AddSingleton<IAlertRepository>(_ => new AlertSqliteRepository(dbPath));
        services.AddSingleton<IAlertService, AlertService>();

        services.AddSingleton<AlertsViewModel>(sp => new AlertsViewModel(
            sp.GetRequiredService<IAlertService>(),
            sp.GetRequiredService<IStockSearchService>(),
            sp.GetRequiredService<IStockService>(),
            sp.GetRequiredService<IScheduler>(),
            sp.GetRequiredService<ISnackbarService>(),
            sp.GetRequiredService<ILocalizationService>(),
            sp.GetService<ICurrencyService>()));

        return services;
    }
}
