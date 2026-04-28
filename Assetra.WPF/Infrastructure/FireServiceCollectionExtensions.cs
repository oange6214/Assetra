using Assetra.Application.Fire;
using Assetra.Core.Interfaces.Fire;
using Assetra.WPF.Features.Fire;
using Microsoft.Extensions.DependencyInjection;

namespace Assetra.WPF.Infrastructure;

internal static class FireServiceCollectionExtensions
{
    public static IServiceCollection AddFireContext(this IServiceCollection services)
    {
        services.AddSingleton<IFireCalculatorService, FireCalculatorService>();
        services.AddSingleton<FireViewModel>();
        return services;
    }
}
