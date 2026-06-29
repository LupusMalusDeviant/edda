using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Edda.Core.Exceptions;

namespace Edda.AKG.Ingestion.Llm;

/// <summary>
/// <see cref="ILlmChatClient"/> backed by the Anthropic Messages API (<c>/v1/messages</c>).
/// Infrastructure adapter (real HTTP) — the response parsing is unit-tested, the network call itself is
/// covered by an optional integration test. The API key comes from configuration and is sent via the
/// <c>x-api-key</c> header.
/// </summary>
public sealed class AnthropicChatClient : ILlmChatClient
{
    private const string AnthropicVersion = "2023-06-01";
    private const int DefaultMaxTokens = 4096;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _baseUrl;
    private readonly string? _apiKey;
    private readonly string _model;

    /// <summary>Initializes a new instance of the <see cref="AnthropicChatClient"/> class.</summary>
    /// <param name="httpClientFactory">Factory used to create the HTTP client.</param>
    /// <param name="baseUrl">API base URL (e.g. <c>https://api.anthropic.com</c>).</param>
    /// <param name="apiKey">Anthropic API key sent via the <c>x-api-key</c> header.</param>
    /// <param name="model">Model identifier (e.g. <c>claude-opus-4-8</c>).</param>
    public AnthropicChatClient(IHttpClientFactory httpClientFactory, string baseUrl, string? apiKey, string model)
    {
        _httpClientFactory = httpClientFactory;
        _baseUrl = baseUrl.TrimEnd('/');
        _apiKey = apiKey;
        _model = model;
    }

    /// <inheritdoc />
    public string ProviderName => "anthropic";

    /// <inheritdoc />
    public async Task<string> CompleteAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default)
    {
        using var client = _httpClientFactory.CreateClient("anthropic-chat");
        client.DefaultRequestHeaders.Add("anthropic-version", AnthropicVersion);
        if (!string.IsNullOrWhiteSpace(_apiKey))
            client.DefaultRequestHeaders.Add("x-api-key", _apiKey);

        var payload = new
        {
            model = _model,
            max_tokens = DefaultMaxTokens,
            system = systemPrompt,
            messages = new[] { new { role = "user", content = userPrompt } },
        };

        HttpResponseMessage response;
        try
        {
            response = await client.PostAsJsonAsync($"{_baseUrl}/v1/messages", payload, cancellationToken)
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
    /// Extracts and concatenates the text blocks from an Anthropic Messages API response; returns empty
    /// for a safety refusal or a response without text content. Internal for testing.
    /// </summary>
    internal static string ParseContent(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (root.TryGetProperty("stop_reason", out var stopReason)
            && stopReason.ValueKind == JsonValueKind.String
            && stopReason.GetString() == "refusal")
        {
            return string.Empty;
        }

        if (!root.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var block in content.EnumerateArray())
        {
            if (block.TryGetProperty("type", out var type)
                && type.ValueKind == JsonValueKind.String
                && type.GetString() == "text"
                && block.TryGetProperty("text", out var text)
                && text.ValueKind == JsonValueKind.String)
            {
                builder.Append(text.GetString());
            }
        }

        return builder.ToString();
    }
}
