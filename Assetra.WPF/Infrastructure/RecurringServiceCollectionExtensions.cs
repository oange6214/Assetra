using Assetra.Core.Interfaces;
using Assetra.Core.Interfaces.Sync;
using Assetra.Infrastructure.Persistence;
using Assetra.WPF.Features.Recurring;
using Assetra.WPF.Features.Snackbar;
using Microsoft.Extensions.DependencyInjection;

namespace Assetra.WPF.Infrastructure;

internal static class RecurringServiceCollectionExtensions
{
    public static IServiceCollection AddRecurringContext(
        this IServiceCollection services,
        string dbPath)
    {
        // v0.20.11: RecurringTransaction shares one instance for repo + sync store.
        services.AddSingleton<RecurringTransactionSqliteRepository>(sp =>
        {
            var settings = sp.GetRequiredService<IAppSettingsService>();
            var deviceId = settings.Current.SyncDeviceId is { Length: > 0 } id ? id : "local";
            return new RecurringTransactionSqliteRepository(dbPath, deviceId);
        });
        services.AddSingleton<IRecurringTransactionRepository>(sp => sp.GetRequiredService<RecurringTransactionSqliteRepository>());
        services.AddSingleton<IRecurringTransactionSyncStore>(sp => sp.GetRequiredService<RecurringTransactionSqliteRepository>());
        services.AddSingleton<IPendingRecurringEntryRepository>(_ => new PendingRecurringEntrySqliteRepository(dbPath));
        services.AddSingleton<Assetra.Application.Recurring.Services.RecurringTransactionScheduler>();

        services.AddSingleton<RecurringViewModel>(sp => new RecurringViewModel(
            sp.GetRequiredService<IRecurringTransactionRepository>(),
            sp.GetRequiredService<IPendingRecurringEntryRepository>(),
            sp.GetRequiredService<Assetra.Application.Recurring.Services.RecurringTransactionScheduler>(),
            sp.GetRequiredService<ISnackbarService>(),
            sp.GetRequiredService<ILocalizationService>()));

        return services;
    }
}
