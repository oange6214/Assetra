using Assetra.Core.Interfaces;

namespace Assetra.Application.Assistant.Llm;

/// <summary>
/// Default LLM provider used when no API key / endpoint is configured.
/// Always returns null so callers fall back to <see cref="RuleBasedFinancialAssistant"/>.
/// </summary>
public sealed class NullLlmProvider : ILlmProvider
{
    public string ProviderId => "null";
    public bool IsConfigured => false;

    public Task<string?> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct = default) =>
        Task.FromResult<string?>(null);
}
