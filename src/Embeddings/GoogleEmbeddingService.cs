using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Edda.Core.Abstractions;
using Edda.Core.Exceptions;
using Microsoft.Extensions.Logging;

namespace Edda.Agent.Providers.Embeddings;

/// <summary>
/// IEmbeddingService implementation using Google's gemini-embedding-001 model (3072 dimensions).
/// </summary>
public sealed class GoogleEmbeddingService : IEmbeddingService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _apiKey;
    private readonly ILogger<GoogleEmbeddingService> _logger;
    private readonly SemaphoreSlim _throttle = new(4, 4);

    /// <inheritdoc/>
    public int Dimensions => 3072;

    /// <inheritdoc/>
    public bool IsAvailable => true;

    /// <summary>
    /// Initializes a new GoogleEmbeddingService.
    /// </summary>
    /// <param name="httpClientFactory">Factory used to create HTTP clients.</param>
    /// <param name="apiKey">Google API key.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    public GoogleEmbeddingService(
        IHttpClientFactory httpClientFactory,
        string apiKey,
        ILogger<GoogleEmbeddingService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _apiKey = apiKey;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        await _throttle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await EmbedInternalAsync(text, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _throttle.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<float[]>> EmbedBatchAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default)
    {
        var tasks = texts.Select(t => EmbedAsync(t, cancellationToken));
        return await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private async Task<float[]> EmbedInternalAsync(string text, CancellationToken ct)
    {
        using var client = _httpClientFactory.CreateClient("google-embedding");
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-embedding-001:embedContent?key={_apiKey}";

        var response = await client.PostAsJsonAsync(url, new
        {
            model = "models/gemini-embedding-001",
            content = new { parts = new[] { new { text } } }
        }, ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            throw new ProviderException("google-embedding", "Embedding request failed.", (int)response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<GoogleEmbedResponse>(ct).ConfigureAwait(false);
        return result?.Embedding?.Values ?? [];
    }

    private sealed class GoogleEmbedResponse
    {
        [JsonPropertyName("embedding")]
        public GoogleEmbedValues? Embedding { get; set; }
    }

    private sealed class GoogleEmbedValues
    {
        [JsonPropertyName("values")]
        public float[]? Values { get; set; }
    }
}
