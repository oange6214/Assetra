using Assetra.Application.Analysis;
using Assetra.Core.Interfaces.Analysis;
using Microsoft.Extensions.DependencyInjection;

namespace Assetra.WPF.Infrastructure;

internal static class AnalysisServiceCollectionExtensions
{
    public static IServiceCollection AddAnalysisContext(this IServiceCollection services)
    {
        services.AddSingleton<IXirrCalculator, XirrCalculator>();
        services.AddSingleton<ITimeWeightedReturnCalculator, TimeWeightedReturnCalculator>();
        services.AddSingleton<IMoneyWeightedReturnCalculator, MoneyWeightedReturnCalculator>();
        services.AddSingleton<IBenchmarkComparisonService, BenchmarkComparisonService>();
        services.AddSingleton<IPnlAttributionService, PnlAttributionService>();
        return services;
    }
}
