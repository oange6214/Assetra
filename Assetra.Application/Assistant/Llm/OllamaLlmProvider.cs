using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Assetra.Core.Interfaces;

namespace Assetra.Application.Assistant.Llm;

/// <summary>
/// Local Ollama provider — talks to <c>http://localhost:11434/api/generate</c>
/// with no auth. Default model: <c>llama3.1:8b</c>. Recommended for users who
/// want LLM-powered planning without sending data to a 3rd party.
///
/// <para>
/// Setup: <c>ollama pull llama3.1:8b</c>; ensure the daemon is running; toggle
/// LlmProvider="ollama" in AppSettings.
/// </para>
/// </summary>
public sealed class OllamaLlmProvider : ILlmProvider
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly string _model;

    public OllamaLlmProvider(HttpClient http, string baseUrl = "http://localhost:11434", string model = "llama3.1:8b")
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _baseUrl = baseUrl.TrimEnd('/');
        _model = model;
    }

    public string ProviderId => $"ollama/{_model}";
    public bool IsConfigured => true;  // Local; assume daemon is reachable

    public async Task<string?> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userPrompt))
            return null;

        var combined = string.IsNullOrWhiteSpace(systemPrompt)
            ? userPrompt
            : $"{systemPrompt}\n\nUser: {userPrompt}\n\nAssistant:";

        try
        {
            var req = new OllamaRequest(_model, combined, Stream: false);
            var resp = await _http.PostAsJsonAsync(
                $"{_baseUrl}/api/generate",
                req,
                ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                throw new LlmProviderException($"Ollama responded with {(int)resp.StatusCode}");
            var body = await resp.Content.ReadFromJsonAsync<OllamaResponse>(cancellationToken: ct)
                .ConfigureAwait(false);
            return body?.Response;
        }
        catch (LlmProviderException) { throw; }
        catch (Exception ex)
        {
            throw new LlmProviderException("Ollama call failed: " + ex.Message, ex);
        }
    }

    private sealed record OllamaRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("prompt")] string Prompt,
        [property: JsonPropertyName("stream")] bool Stream);

    private sealed record OllamaResponse(
        [property: JsonPropertyName("response")] string Response);
}
