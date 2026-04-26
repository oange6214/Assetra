using Assetra.Core.Interfaces;
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
        services.AddSingleton<IRecurringTransactionRepository>(_ => new RecurringTransactionSqliteRepository(dbPath));
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
