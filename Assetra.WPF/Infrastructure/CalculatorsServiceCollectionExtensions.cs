using Assetra.Application.Calculators;
using Assetra.WPF.Features.Calculators;
using Microsoft.Extensions.DependencyInjection;

namespace Assetra.WPF.Infrastructure;

internal static class CalculatorsServiceCollectionExtensions
{
    public static IServiceCollection AddCalculatorsContext(this IServiceCollection services)
    {
        services.AddSingleton<LoanAmortizationService>();
        services.AddSingleton<LatteFactorCalculator>();
        services.AddSingleton<RuleOf72Calculator>();
        services.AddSingleton<RentVsBuyCalculator>();
        services.AddSingleton<LoanCalcViewModel>();
        services.AddSingleton<LatteFactorCalcViewModel>();
        services.AddSingleton<RuleOf72CalcViewModel>();
        services.AddSingleton<RentVsBuyCalcViewModel>();
        services.AddSingleton<CalculatorsViewModel>();
        return services;
    }
}
