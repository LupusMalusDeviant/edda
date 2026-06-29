using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Edda.Core.Abstractions;
using Edda.Core.Exceptions;
using Microsoft.Extensions.Logging;

namespace Edda.Agent.Providers.Embeddings;

/// <summary>
/// IEmbeddingService implementation using OpenAI's text-embedding-3-small model (1536 dimensions).
/// </summary>
public sealed class OpenAiEmbeddingService : IEmbeddingService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _apiKey;
    private readonly ILogger<OpenAiEmbeddingService> _logger;
    private readonly SemaphoreSlim _throttle = new(4, 4);

    /// <inheritdoc/>
    public int Dimensions => 1536;

    /// <inheritdoc/>
    public bool IsAvailable => true;

    /// <summary>
    /// Initializes a new OpenAiEmbeddingService.
    /// </summary>
    /// <param name="httpClientFactory">Factory used to create HTTP clients.</param>
    /// <param name="apiKey">OpenAI API key.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    public OpenAiEmbeddingService(
        IHttpClientFactory httpClientFactory,
        string apiKey,
        ILogger<OpenAiEmbeddingService> logger)
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
        using var client = _httpClientFactory.CreateClient("openai-embedding");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        var response = await client.PostAsJsonAsync(
            "https://api.openai.com/v1/embeddings",
            new { input = text, model = "text-embedding-3-small" },
            ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            throw new ProviderException("openai-embedding", "Embedding request failed.", (int)response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<EmbeddingResponse>(ct).ConfigureAwait(false);
        return result?.Data?.FirstOrDefault()?.Embedding ?? [];
    }

    private sealed class EmbeddingResponse
    {
        [JsonPropertyName("data")]
        public List<EmbeddingData>? Data { get; set; }
    }

    private sealed class EmbeddingData
    {
        [JsonPropertyName("embedding")]
        public float[]? Embedding { get; set; }
    }
}
