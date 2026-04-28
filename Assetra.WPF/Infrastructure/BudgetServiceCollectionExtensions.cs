using Assetra.Core.Interfaces;
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
        // Repositories
        services.AddSingleton<ICategoryRepository>(_ => new CategorySqliteRepository(dbPath));
        services.AddSingleton<IAutoCategorizationRuleRepository>(_ => new AutoCategorizationRuleSqliteRepository(dbPath));
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
