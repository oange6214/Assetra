using Assetra.Application.MonteCarlo;
using Assetra.Core.Interfaces.MonteCarlo;
using Assetra.WPF.Features.MonteCarlo;
using Microsoft.Extensions.DependencyInjection;

namespace Assetra.WPF.Infrastructure;

internal static class MonteCarloServiceCollectionExtensions
{
    public static IServiceCollection AddMonteCarloContext(this IServiceCollection services)
    {
        services.AddSingleton<IMonteCarloSimulator, MonteCarloSimulator>();
        services.AddSingleton<MonteCarloViewModel>();
        return services;
    }
}
