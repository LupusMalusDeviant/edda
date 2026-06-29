using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Edda.Core.Exceptions;

namespace Edda.AKG.Ingestion.Llm;

/// <summary>
/// <see cref="ILlmChatClient"/> backed by an OpenAI-compatible chat-completions API (OpenRouter by
/// default). Infrastructure adapter (real HTTP) — the response parsing is unit-tested, the network call
/// itself is covered by an optional integration test. The API key comes from configuration.
/// </summary>
public sealed class OpenRouterChatClient : ILlmChatClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _baseUrl;
    private readonly string? _apiKey;
    private readonly string _model;

    /// <summary>Initializes a new instance of the <see cref="OpenRouterChatClient"/> class.</summary>
    /// <param name="httpClientFactory">Factory used to create the HTTP client.</param>
    /// <param name="baseUrl">API base URL (e.g. <c>https://openrouter.ai/api/v1</c>).</param>
    /// <param name="apiKey">Bearer token for the API.</param>
    /// <param name="model">Model identifier (e.g. <c>meta-llama/llama-3.1-70b-instruct</c>).</param>
    public OpenRouterChatClient(IHttpClientFactory httpClientFactory, string baseUrl, string? apiKey, string model)
    {
        _httpClientFactory = httpClientFactory;
        _baseUrl = baseUrl.TrimEnd('/');
        _apiKey = apiKey;
        _model = model;
    }

    /// <inheritdoc />
    public string ProviderName => "openrouter";

    /// <inheritdoc />
    public async Task<string> CompleteAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default)
    {
        using var client = _httpClientFactory.CreateClient("openrouter-chat");
        if (!string.IsNullOrWhiteSpace(_apiKey))
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        var payload = new
        {
            model = _model,
            temperature = 0.2,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt },
            },
        };

        HttpResponseMessage response;
        try
        {
            response = await client.PostAsJsonAsync($"{_baseUrl}/chat/completions", payload, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new ProviderException(ProviderName, $"Chat request failed: {ex.Message}", innerException: ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new ProviderException(
                ProviderName,
                $"Chat completion returned {(int)response.StatusCode}: {error}",
                (int)response.StatusCode);
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return ParseContent(json);
    }

    /// <summary>Extracts <c>choices[0].message.content</c> from an OpenAI-compatible response. Internal for testing.</summary>
    internal static string ParseContent(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.TryGetProperty("choices", out var choices)
            && choices.ValueKind == JsonValueKind.Array
            && choices.GetArrayLength() > 0
            && choices[0].TryGetProperty("message", out var message)
            && message.TryGetProperty("content", out var content)
            && content.ValueKind == JsonValueKind.String)
        {
            return content.GetString() ?? string.Empty;
        }

        return string.Empty;
    }
}
