using Assetra.Application.Assistant;
using Assetra.Core.Interfaces;
using Assetra.WPF.Features.Assistant;
using Microsoft.Extensions.DependencyInjection;

namespace Assetra.WPF.Infrastructure;

internal static class AssistantServiceCollectionExtensions
{
    /// <summary>
    /// Wires the Phase 1 rule-based <see cref="IFinancialAssistant"/> + its
    /// <see cref="AssistantViewModel"/>. Future phases (LLM provider, history
    /// persistence) layer on top by replacing the singleton registration.
    /// </summary>
    public static IServiceCollection AddAssistantContext(this IServiceCollection services)
    {
        services.AddSingleton<IFinancialAssistant>(sp => new RuleBasedFinancialAssistant(
            sp.GetRequiredService<IBalanceQueryService>(),
            sp.GetService<IAppSettingsService>(),
            sp.GetService<ICurrencyService>()));
        services.AddSingleton<IAssistantInsightService>(sp => new RuleBasedAssistantInsightService(
            sp.GetService<IBudgetRepository>(),
            sp.GetService<IRecurringTransactionRepository>(),
            sp.GetService<ITradeRepository>()));
        services.AddSingleton<AssistantViewModel>();
        return services;
    }
}
