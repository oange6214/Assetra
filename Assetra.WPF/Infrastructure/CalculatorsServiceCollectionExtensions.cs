using Assetra.Application.Calculators;
using Assetra.WPF.Features.Calculators;
using Microsoft.Extensions.DependencyInjection;

namespace Assetra.WPF.Infrastructure;

internal static class CalculatorsServiceCollectionExtensions
{
    public static IServiceCollection AddCalculatorsContext(this IServiceCollection services)
    {
        services.AddSingleton<LoanAmortizationService>();
        services.AddSingleton<RentVsBuyCalculator>();
        services.AddSingleton<LoanCalcViewModel>();
        services.AddSingleton<RentVsBuyCalcViewModel>();
        services.AddSingleton<CalculatorsViewModel>();
        return services;
    }
}
