using Assetra.Core.Interfaces;

namespace Assetra.Application.Assistant;

/// <summary>
/// AI Phase 3 — orchestrates rule-based + LLM. Tries the rule-based dispatch
/// first (fast + grounded + free); only falls through to LLM when the rule
/// base returns Unhandled AND a configured <see cref="ILlmProvider"/> is
/// available.
///
/// <para>
/// This keeps the deterministic answers cheap while letting LLM handle
/// open-ended questions ("how can I save more next month?") that rules
/// can't pattern-match. Tool/function calling for grounded LLM queries is
/// future work (Phase 3.5) — currently the LLM only sees the user's text,
/// not the financial data.
/// </para>
/// </summary>
public sealed class HybridFinancialAssistant : IFinancialAssistant
{
    private readonly RuleBasedFinancialAssistant _ruleBased;
    private readonly ILlmProvider _llm;
    private readonly IAssistantToolRegistry? _tools;

    public HybridFinancialAssistant(
        RuleBasedFinancialAssistant ruleBased,
        ILlmProvider llm,
        IAssistantToolRegistry? tools = null)
    {
        _ruleBased = ruleBased ?? throw new ArgumentNullException(nameof(ruleBased));
        _llm = llm ?? throw new ArgumentNullException(nameof(llm));
        _tools = tools;
    }

    public IReadOnlyList<string> SuggestedQueries => _ruleBased.SuggestedQueries;

    public async Task<FinancialAssistantResponse> AnswerAsync(
        FinancialAssistantQuery query, CancellationToken ct = default)
    {
        // 1. Rule-based first (deterministic, free, grounded).
        var ruleResponse = await _ruleBased.AnswerAsync(query, ct).ConfigureAwait(false);
        if (ruleResponse.IsHandled || !_llm.IsConfigured)
            return ruleResponse;

        // 2. Phase 3.5 — pre-fetch grounded data from the tool registry and
        //    splice into the system prompt before calling the LLM. This gives
        //    the model real numbers to reason over and dramatically reduces
        //    hallucinations on numeric questions.
        var groundedContext = await BuildGroundedContextAsync(ct).ConfigureAwait(false);
        var systemPrompt =
            "你是個人理財助手。回答簡潔、實用。若問題涉及具體數字而你沒有數據，請坦白回覆。\n" +
            (string.IsNullOrEmpty(groundedContext) ? string.Empty :
                "\n以下是使用者目前的財務快照（grounding data）：\n" + groundedContext);

        // 3. LLM fallback for open-ended questions.
        try
        {
            var answer = await _llm.CompleteAsync(
                systemPrompt: systemPrompt,
                userPrompt: query.Text,
                ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(answer))
                return ruleResponse;
            return new FinancialAssistantResponse(
                Answer: answer,
                Source: _llm.ProviderId);
        }
        catch (LlmProviderException ex)
        {
            return new FinancialAssistantResponse(
                Answer: $"LLM 查詢失敗（{ex.Message}）。\n{ruleResponse.Answer}",
                Source: ruleResponse.Source,
                IsHandled: false);
        }
    }

    private async Task<string> BuildGroundedContextAsync(CancellationToken ct)
    {
        if (_tools is null || _tools.Tools.Count == 0) return string.Empty;
        var snippets = new List<string>();
        // Phase 3.5 MVP: invoke ALL tools (each returns a short snapshot string
        // — total context << 1k chars). A future revision routes per-query:
        // LLM picks which tool(s) to invoke via OpenAI-style function calling.
        foreach (var tool in _tools.Tools)
        {
            try
            {
                var result = await tool.InvokeAsync(ct).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(result))
                    snippets.Add($"- {tool.Name}: {result}");
            }
            catch
            {
                // Skip tools that throw — partial grounding is better than no answer.
            }
        }
        return string.Join('\n', snippets);
    }
}
