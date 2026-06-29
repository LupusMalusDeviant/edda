using System.Net.Http.Headers;
using Edda.Core.Abstractions;
using Edda.Core.Models;
using Edda.Core.Providers;

namespace Edda.Web.Services;

/// <summary>
/// <see cref="IProviderProbe"/> implementation performing the actual HTTP calls to provider model-listing
/// endpoints (Ollama <c>/api/tags</c>, OpenAI-compatible <c>/v1/models</c>, Anthropic, Google/Gemini).
/// Infrastructure adapter — response parsing is unit-tested via <see cref="ProviderModelParser"/>; the
/// network calls here are not. All errors are caught and returned as a failed <see cref="ProbeResult"/>.
/// </summary>
public sealed class ProviderProbe : IProviderProbe
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(10);

    private readonly IHttpClientFactory _httpClientFactory;

    /// <summary>Initializes a new instance of the <see cref="ProviderProbe"/> class.</summary>
    /// <param name="httpClientFactory">Factory used to create the probe HTTP client.</param>
    public ProviderProbe(IHttpClientFactory httpClientFactory) => _httpClientFactory = httpClientFactory;

    /// <inheritdoc />
    public async Task<ProbeResult> ProbeAsync(
        string provider,
        string? baseUrl,
        string? apiKey,
        CancellationToken cancellationToken = default)
    {
        var key = (provider ?? string.Empty).Trim().ToLowerInvariant();
        try
        {
            return key switch
            {
                "ollama" => await ListAsync(
                    Url(baseUrl, "http://localhost:11434", "/api/tags"), apiKey,
                    ProviderModelParser.ParseOllamaTags, "Ollama", cancellationToken).ConfigureAwait(false),
                "openai" => await ListAsync(
                    Url(baseUrl, "https://api.openai.com/v1", "/models"), apiKey,
                    ProviderModelParser.ParseOpenAiModels, "OpenAI", cancellationToken).ConfigureAwait(false),
                "openrouter" => await ListAsync(
                    Url(baseUrl, "https://openrouter.ai/api/v1", "/models"), apiKey,
                    ProviderModelParser.ParseOpenAiModels, "OpenRouter", cancellationToken).ConfigureAwait(false),
                "voyage" => await ListAsync(
                    Url(baseUrl, "https://api.voyageai.com/v1", "/models"), apiKey,
                    ProviderModelParser.ParseOpenAiModels, "Voyage", cancellationToken).ConfigureAwait(false),
                "custom" => string.IsNullOrWhiteSpace(baseUrl)
                    ? Unsupported("Für 'custom' bitte zuerst eine Base-URL angeben.")
                    : await ListAsync(
                        Url(baseUrl, baseUrl, "/models"), apiKey,
                        ProviderModelParser.ParseOpenAiModels, "Custom", cancellationToken).ConfigureAwait(false),
                "anthropic" => await ProbeAnthropicAsync(baseUrl, apiKey, cancellationToken).ConfigureAwait(false),
                "gemini" or "google" => await ProbeGeminiAsync(baseUrl, apiKey, cancellationToken).ConfigureAwait(false),
                "bedrock" => Unsupported(
                    "Verbindungstest/Modell-Liste für AWS Bedrock werden nicht unterstützt (SigV4) — Modell manuell eintragen."),
                "null" or "" => Unsupported("Kein Provider gewählt."),
                _ => Unsupported($"Verbindungstest für Provider '{provider}' nicht unterstützt."),
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
                                       or InvalidOperationException or UriFormatException)
        {
            return new ProbeResult { Ok = false, Message = $"✗ Verbindung fehlgeschlagen: {ex.Message}" };
        }
    }

    private async Task<ProbeResult> ListAsync(
        string url, string? apiKey, Func<string, IReadOnlyList<string>> parse, string label, CancellationToken ct)
    {
        using var client = _httpClientFactory.CreateClient("provider-probe");
        client.Timeout = ProbeTimeout;
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrWhiteSpace(apiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var response = await client.SendAsync(request, ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return new ProbeResult { Ok = false, Message = $"✗ {label}: HTTP {(int)response.StatusCode}." };

        var models = parse(body);
        return new ProbeResult
        {
            Ok = true,
            Message = $"✓ {label} erreichbar — {models.Count} Modell(e) gefunden.",
            Models = models,
        };
    }

    private async Task<ProbeResult> ProbeAnthropicAsync(string? baseUrl, string? apiKey, CancellationToken ct)
    {
        using var client = _httpClientFactory.CreateClient("provider-probe");
        client.Timeout = ProbeTimeout;
        using var request = new HttpRequestMessage(
            HttpMethod.Get, Url(baseUrl, "https://api.anthropic.com", "/v1/models"));
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            request.Headers.Add("x-api-key", apiKey);
            request.Headers.Add("anthropic-version", "2023-06-01");
        }

        using var response = await client.SendAsync(request, ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return new ProbeResult { Ok = false, Message = $"✗ Anthropic: HTTP {(int)response.StatusCode}." };

        var models = ProviderModelParser.ParseOpenAiModels(body);
        return new ProbeResult { Ok = true, Message = $"✓ Anthropic erreichbar — {models.Count} Modell(e).", Models = models };
    }

    private async Task<ProbeResult> ProbeGeminiAsync(string? baseUrl, string? apiKey, CancellationToken ct)
    {
        var resolved = (string.IsNullOrWhiteSpace(baseUrl)
            ? "https://generativelanguage.googleapis.com/v1beta" : baseUrl!).TrimEnd('/');
        var url = $"{resolved}/models";
        if (!string.IsNullOrWhiteSpace(apiKey))
            url += $"?key={Uri.EscapeDataString(apiKey)}";

        using var client = _httpClientFactory.CreateClient("provider-probe");
        client.Timeout = ProbeTimeout;
        using var response = await client.GetAsync(url, ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return new ProbeResult { Ok = false, Message = $"✗ Google/Gemini: HTTP {(int)response.StatusCode}." };

        var models = ProviderModelParser.ParseGeminiModels(body);
        return new ProbeResult
        {
            Ok = true, Message = $"✓ Google/Gemini erreichbar — {models.Count} Modell(e).", Models = models,
        };
    }

    private static string Url(string? baseUrl, string fallback, string path)
        => (string.IsNullOrWhiteSpace(baseUrl) ? fallback : baseUrl!).TrimEnd('/') + path;

    private static ProbeResult Unsupported(string message) => new() { Ok = false, Message = message, Models = [] };
}
