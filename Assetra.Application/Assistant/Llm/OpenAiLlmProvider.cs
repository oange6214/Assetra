using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Assetra.Core.Interfaces;

namespace Assetra.Application.Assistant.Llm;

/// <summary>
/// OpenAI Chat Completions provider. Default model: <c>gpt-4o-mini</c> (cheap+fast).
///
/// <para>
/// Setup: AppSettings.LlmApiKey + LlmProvider="openai". The provider sends only
/// the explicit prompts — caller is responsible for redacting PII before passing
/// values that reference account names/numbers/balances.
/// </para>
/// </summary>
public sealed class OpenAiLlmProvider : ILlmProvider
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _model;

    public OpenAiLlmProvider(HttpClient http, string apiKey, string model = "gpt-4o-mini")
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
        _model = model;
    }

    public string ProviderId => $"openai/{_model}";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey);

    public async Task<string?> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userPrompt)) return null;
        if (!IsConfigured) throw new LlmProviderException("OpenAI API key not configured");

        var req = new OpenAiRequest(
            _model,
            new OpenAiMessage[]
            {
                new("system", systemPrompt ?? string.Empty),
                new("user", userPrompt),
            },
            Temperature: 0.3);

        try
        {
            using var msg = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions")
            {
                Content = JsonContent.Create(req),
            };
            msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            using var resp = await _http.SendAsync(msg, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                throw new LlmProviderException($"OpenAI responded with {(int)resp.StatusCode}");
            var body = await resp.Content.ReadFromJsonAsync<OpenAiResponse>(cancellationToken: ct)
                .ConfigureAwait(false);
            return body?.Choices.FirstOrDefault()?.Message?.Content;
        }
        catch (LlmProviderException) { throw; }
        catch (Exception ex)
        {
            throw new LlmProviderException("OpenAI call failed: " + ex.Message, ex);
        }
    }

    private sealed record OpenAiRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] OpenAiMessage[] Messages,
        [property: JsonPropertyName("temperature")] double Temperature);

    private sealed record OpenAiMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    private sealed record OpenAiResponse(
        [property: JsonPropertyName("choices")] OpenAiChoice[] Choices);

    private sealed record OpenAiChoice(
        [property: JsonPropertyName("message")] OpenAiMessage Message);
}
