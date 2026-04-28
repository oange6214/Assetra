using Assetra.Core.Interfaces;
using Assetra.Core.Interfaces.Sync;
using Assetra.Infrastructure.Persistence;
using Assetra.WPF.Features.Categories;
using Assetra.WPF.Features.Portfolio;
using Assetra.WPF.Features.Snackbar;
using Microsoft.Extensions.DependencyInjection;

namespace Assetra.WPF.Infrastructure;

internal static class BudgetServiceCollectionExtensions
{
    public static IServiceCollection AddBudgetContext(
        this IServiceCollection services,
        string dbPath)
    {
        // Repositories — single CategorySqliteRepository instance is shared between
        // ICategoryRepository (user-facing) and ICategorySyncStore (sync layer).
        services.AddSingleton<CategorySqliteRepository>(sp =>
        {
            var settings = sp.GetRequiredService<IAppSettingsService>();
            var deviceId = settings.Current.SyncDeviceId is { Length: > 0 } id ? id : "local";
            return new CategorySqliteRepository(dbPath, deviceId);
        });
        services.AddSingleton<ICategoryRepository>(sp => sp.GetRequiredService<CategorySqliteRepository>());
        services.AddSingleton<ICategorySyncStore>(sp => sp.GetRequiredService<CategorySqliteRepository>());

        // v0.20.11: AutoCategorizationRule shares one instance for repo + sync store.
        services.AddSingleton<AutoCategorizationRuleSqliteRepository>(sp =>
        {
            var settings = sp.GetRequiredService<IAppSettingsService>();
            var deviceId = settings.Current.SyncDeviceId is { Length: > 0 } id ? id : "local";
            return new AutoCategorizationRuleSqliteRepository(dbPath, deviceId);
        });
        services.AddSingleton<IAutoCategorizationRuleRepository>(sp => sp.GetRequiredService<AutoCategorizationRuleSqliteRepository>());
        services.AddSingleton<IAutoCategorizationRuleSyncStore>(sp => sp.GetRequiredService<AutoCategorizationRuleSqliteRepository>());
        services.AddSingleton<IBudgetRepository>(_ => new BudgetSqliteRepository(dbPath));

        // Application services
        services.AddSingleton<Assetra.Application.Budget.Services.MonthlyBudgetSummaryService>();

        // Cross-VM notifier (Budget-scoped)
        services.AddSingleton<IBudgetRefreshNotifier, BudgetRefreshNotifier>();

        // ViewModels
        services.AddSingleton<BudgetSummaryCardViewModel>(sp => new BudgetSummaryCardViewModel(
            sp.GetRequiredService<Assetra.Application.Budget.Services.MonthlyBudgetSummaryService>(),
            sp.GetRequiredService<IBudgetRefreshNotifier>()));
        services.AddSingleton<CategoriesViewModel>(sp => new CategoriesViewModel(
            sp.GetRequiredService<ICategoryRepository>(),
            sp.GetRequiredService<IAutoCategorizationRuleRepository>(),
            sp.GetRequiredService<IBudgetRepository>(),
            sp.GetRequiredService<ITradeRepository>(),
            sp.GetRequiredService<IRecurringTransactionRepository>(),
            sp.GetRequiredService<IPendingRecurringEntryRepository>(),
            sp.GetRequiredService<IBudgetRefreshNotifier>(),
            sp.GetRequiredService<ISnackbarService>(),
            sp.GetRequiredService<ILocalizationService>()));

        return services;
    }
}
