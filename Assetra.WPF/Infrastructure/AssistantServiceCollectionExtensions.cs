using System.Net.Http;
using Assetra.Application.Assistant;
using Assetra.Application.Assistant.Llm;
using Assetra.Core.Interfaces;
using Assetra.WPF.Features.Assistant;
using Microsoft.Extensions.DependencyInjection;

namespace Assetra.WPF.Infrastructure;

internal static class AssistantServiceCollectionExtensions
{
    /// <summary>
    /// Wires the rule-based assistant (Phase 1) + insights service (Phase 2) +
    /// optional LLM provider (Phase 3). The hybrid orchestrator picks rule-based
    /// when it can answer, LLM otherwise.
    /// </summary>
    public static IServiceCollection AddAssistantContext(this IServiceCollection services)
    {
        services.AddSingleton<RuleBasedFinancialAssistant>(sp => new RuleBasedFinancialAssistant(
            sp.GetRequiredService<IBalanceQueryService>(),
            sp.GetService<IAppSettingsService>(),
            sp.GetService<ICurrencyService>()));

        services.AddSingleton<ILlmProvider>(sp =>
        {
            var settings = sp.GetService<IAppSettingsService>()?.Current;
            var provider = (settings?.LlmProvider ?? string.Empty).Trim().ToLowerInvariant();
            return provider switch
            {
                "openai" when !string.IsNullOrWhiteSpace(settings!.LlmApiKey) =>
                    new OpenAiLlmProvider(
                        new HttpClient { Timeout = TimeSpan.FromSeconds(30) },
                        settings.LlmApiKey,
                        string.IsNullOrWhiteSpace(settings.LlmModel) ? "gpt-4o-mini" : settings.LlmModel),
                "ollama" =>
                    new OllamaLlmProvider(
                        new HttpClient { Timeout = TimeSpan.FromSeconds(60) },
                        string.IsNullOrWhiteSpace(settings!.LlmEndpoint) ? "http://localhost:11434" : settings.LlmEndpoint,
                        string.IsNullOrWhiteSpace(settings.LlmModel) ? "llama3.1:8b" : settings.LlmModel),
                _ => new NullLlmProvider(),
            };
        });

        services.AddSingleton<IFinancialAssistant>(sp => new HybridFinancialAssistant(
            sp.GetRequiredService<RuleBasedFinancialAssistant>(),
            sp.GetRequiredService<ILlmProvider>()));

        services.AddSingleton<IAssistantInsightService>(sp => new RuleBasedAssistantInsightService(
            sp.GetService<IBudgetRepository>(),
            sp.GetService<IRecurringTransactionRepository>(),
            sp.GetService<ITradeRepository>()));
        services.AddSingleton<AssistantViewModel>();

        // Phase 2 scheduler — polls insights every 4h and pushes Critical/Warning
        // to the snackbar. Registered as IHostedService so it starts/stops with the host.
        services.AddHostedService<AssistantInsightHostedService>();
        return services;
    }
}
