using Assetra.Application.Goals;
using Assetra.Core.Interfaces;
using Assetra.Infrastructure.Persistence;
using Assetra.WPF.Features.PortfolioGroups;
using Microsoft.Extensions.DependencyInjection;

namespace Assetra.WPF.Infrastructure;

/// <summary>
/// DI registration for Portfolio-Groups-Refactor P1 + P2 — exposes
/// <see cref="IPortfolioGroupRepository"/> backed by SQLite plus the
/// <see cref="PortfolioGroupsViewModel"/> CRUD page VM.
/// Schema migration (table creation + default group seed) runs lazily in
/// the repo constructor.
/// </summary>
internal static class PortfolioGroupsServiceCollectionExtensions
{
    public static IServiceCollection AddPortfolioGroupsContext(
        this IServiceCollection services,
        string dbPath)
    {
        services.AddSingleton<IPortfolioGroupRepository>(_ => new PortfolioGroupSqliteRepository(dbPath));
        services.AddSingleton<PortfolioGroupCatalog>();
        // Portfolio-Groups-Refactor P5 — Goal auto-tracking 用的 per-group 淨值計算。
        services.AddSingleton<IGroupBalanceQueryService>(sp =>
            new GroupBalanceQueryService(sp.GetRequiredService<ITradeRepository>()));
        services.AddSingleton<PortfolioGroupsViewModel>();
        return services;
    }
}
