namespace Assetra.Core.Interfaces;

/// <summary>
/// AI Phase 3 — LLM provider abstraction. Pluggable backends (OpenAI / Anthropic /
/// local Ollama / null) all implement this single contract so the assistant
/// orchestrator stays provider-agnostic.
///
/// <para>
/// MVP scope: single-shot prompt → text completion. Tool/function calling
/// (for grounded queries against IBalanceQueryService etc.) is planned for
/// Phase 3.5 — added as a separate ITool* interface; <see cref="ILlmProvider"/>
/// stays minimal here.
/// </para>
///
/// <para>
/// All implementations MUST handle:
/// <list type="bullet">
///   <item>Cancellation via the supplied <see cref="CancellationToken"/></item>
///   <item>Network failure → throw <see cref="LlmProviderException"/></item>
///   <item>Empty / whitespace-only prompt → return null (caller skips)</item>
/// </list>
/// </para>
/// </summary>
public interface ILlmProvider
{
    /// <summary>Stable identifier for telemetry/UI ("openai/gpt-4o-mini", "ollama/llama3.1:8b", "null").</summary>
    string ProviderId { get; }

    /// <summary>True when the provider is properly configured (api key set, endpoint reachable).</summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Sends <paramref name="systemPrompt"/> + <paramref name="userPrompt"/> to the model.
    /// Returns null on empty input. Throws <see cref="LlmProviderException"/> on
    /// transport failure.
    /// </summary>
    Task<string?> CompleteAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken ct = default);
}

/// <summary>Wraps any provider-side error (HTTP non-2xx, deserialisation failure, timeout).</summary>
public sealed class LlmProviderException(string message, Exception? inner = null)
    : Exception(message, inner);
