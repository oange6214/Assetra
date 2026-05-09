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

    public HybridFinancialAssistant(RuleBasedFinancialAssistant ruleBased, ILlmProvider llm)
    {
        _ruleBased = ruleBased ?? throw new ArgumentNullException(nameof(ruleBased));
        _llm = llm ?? throw new ArgumentNullException(nameof(llm));
    }

    public IReadOnlyList<string> SuggestedQueries => _ruleBased.SuggestedQueries;

    public async Task<FinancialAssistantResponse> AnswerAsync(
        FinancialAssistantQuery query, CancellationToken ct = default)
    {
        // 1. Rule-based first (deterministic, free, grounded).
        var ruleResponse = await _ruleBased.AnswerAsync(query, ct).ConfigureAwait(false);
        if (ruleResponse.IsHandled || !_llm.IsConfigured)
            return ruleResponse;

        // 2. LLM fallback for open-ended questions.
        try
        {
            var answer = await _llm.CompleteAsync(
                systemPrompt: "你是個人理財助手。回答簡潔、實用。若問題涉及具體數字而你沒有數據，請坦白回覆。",
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
}
