using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Edda.Core.Exceptions;

namespace Edda.AKG.Ingestion.Llm;

/// <summary>
/// <see cref="ILlmChatClient"/> backed by the Google Gemini Generative Language API
/// (<c>/models/{model}:generateContent</c>). Infrastructure adapter (real HTTP); the response parsing
/// is unit-tested. The API key comes from configuration and is sent via the <c>x-goog-api-key</c> header.
/// </summary>
public sealed class GeminiChatClient : ILlmChatClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _baseUrl;
    private readonly string? _apiKey;
    private readonly string _model;

    /// <summary>Initializes a new instance of the <see cref="GeminiChatClient"/> class.</summary>
    /// <param name="httpClientFactory">Factory used to create the HTTP client.</param>
    /// <param name="baseUrl">API base URL (e.g. <c>https://generativelanguage.googleapis.com/v1beta</c>).</param>
    /// <param name="apiKey">Google API key sent via the <c>x-goog-api-key</c> header.</param>
    /// <param name="model">Model identifier (e.g. <c>gemini-1.5-flash</c>).</param>
    public GeminiChatClient(IHttpClientFactory httpClientFactory, string baseUrl, string? apiKey, string model)
    {
        _httpClientFactory = httpClientFactory;
        _baseUrl = baseUrl.TrimEnd('/');
        _apiKey = apiKey;
        _model = model;
    }

    /// <inheritdoc />
    public string ProviderName => "gemini";

    /// <inheritdoc />
    public async Task<string> CompleteAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default)
    {
        using var client = _httpClientFactory.CreateClient("gemini-chat");
        if (!string.IsNullOrWhiteSpace(_apiKey))
            client.DefaultRequestHeaders.Add("x-goog-api-key", _apiKey);

        var payload = new
        {
            systemInstruction = new { parts = new[] { new { text = systemPrompt } } },
            contents = new[] { new { role = "user", parts = new[] { new { text = userPrompt } } } },
        };

        HttpResponseMessage response;
        try
        {
            response = await client.PostAsJsonAsync(
                    $"{_baseUrl}/models/{_model}:generateContent", payload, cancellationToken)
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

    /// <summary>
    /// Extracts and concatenates the text parts from a Gemini <c>generateContent</c> response. Internal for testing.
    /// </summary>
    internal static string ParseContent(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.TryGetProperty("candidates", out var candidates)
            && candidates.ValueKind == JsonValueKind.Array
            && candidates.GetArrayLength() > 0
            && candidates[0].TryGetProperty("content", out var content)
            && content.TryGetProperty("parts", out var parts)
            && parts.ValueKind == JsonValueKind.Array)
        {
            var builder = new StringBuilder();
            foreach (var part in parts.EnumerateArray())
            {
                if (part.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                {
                    builder.Append(text.GetString());
                }
            }

            return builder.ToString();
        }

        return string.Empty;
    }
}
