using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Edda.Core.Exceptions;

namespace Edda.AKG.Ingestion.Llm;

/// <summary>
/// <see cref="ILlmChatClient"/> backed by a local Ollama instance (<c>/api/chat</c>, non-streaming).
/// Infrastructure adapter (real HTTP) — response parsing is unit-tested, the network call is covered by
/// an optional integration test.
/// </summary>
public sealed class OllamaChatClient : ILlmChatClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _baseUrl;
    private readonly string _model;
    private readonly string? _apiKey;

    /// <summary>Initializes a new instance of the <see cref="OllamaChatClient"/> class.</summary>
    /// <param name="httpClientFactory">Factory used to create the HTTP client.</param>
    /// <param name="baseUrl">Ollama base URL (e.g. <c>http://localhost:11434</c>).</param>
    /// <param name="model">Model name (e.g. <c>llama3.1</c>).</param>
    /// <param name="apiKey">Optional bearer token for secured/hosted Ollama endpoints (null for a local instance).</param>
    public OllamaChatClient(IHttpClientFactory httpClientFactory, string baseUrl, string model, string? apiKey = null)
    {
        _httpClientFactory = httpClientFactory;
        _baseUrl = baseUrl.TrimEnd('/');
        _model = model;
        _apiKey = apiKey;
    }

    /// <inheritdoc />
    public string ProviderName => "ollama";

    /// <inheritdoc />
    public async Task<string> CompleteAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default)
    {
        using var client = _httpClientFactory.CreateClient("ollama-chat");

        var payload = new
        {
            model = _model,
            stream = false,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt },
            },
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/api/chat")
        {
            Content = JsonContent.Create(payload),
        };
        if (!string.IsNullOrWhiteSpace(_apiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
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

    /// <summary>Extracts <c>message.content</c> from an Ollama <c>/api/chat</c> response. Internal for testing.</summary>
    internal static string ParseContent(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.TryGetProperty("message", out var message)
            && message.TryGetProperty("content", out var content)
            && content.ValueKind == JsonValueKind.String)
        {
            return content.GetString() ?? string.Empty;
        }

        return string.Empty;
    }
}
