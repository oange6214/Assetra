namespace Assetra.Core.Interfaces;

/// <summary>
/// AI Phase 3.5 — tool/function-calling. The orchestrator (HybridFinancialAssistant
/// or any LLM-aware variant) consults this registry to enrich the user's prompt
/// with grounded data BEFORE sending to the LLM, eliminating hallucinated numbers.
///
/// <para>
/// Each <see cref="AssistantTool"/> has a name, a one-line description (used as
/// the system-prompt hint), and an async invoker that returns a string snippet
/// the orchestrator can splice into the LLM context. The MVP keeps tools
/// stateless and parameter-less (each tool exposes a fixed snapshot like
/// "current net worth"); a future revision will add JSON-schema parameters
/// + multi-turn tool/function-call protocol matching OpenAI's spec.
/// </para>
/// </summary>
public interface IAssistantToolRegistry
{
    IReadOnlyList<AssistantTool> Tools { get; }

    /// <summary>Look up a single tool by name, null if not found.</summary>
    AssistantTool? Find(string name);
}

/// <summary>
/// One callable tool exposed to the assistant. <see cref="InvokeAsync"/> returns
/// a short, model-friendly snippet (a few hundred chars) that grounds the
/// LLM's answer.
/// </summary>
/// <param name="Name">Stable identifier (e.g. "get_net_worth", "list_cash_balances").</param>
/// <param name="Description">One-line natural-language description for the system prompt.</param>
/// <param name="InvokeAsync">Returns the grounded data as a string snippet.</param>
public sealed record AssistantTool(
    string Name,
    string Description,
    Func<CancellationToken, Task<string>> InvokeAsync);
