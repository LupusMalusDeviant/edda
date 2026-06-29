using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Edda.Core.Abstractions;
using Edda.Core.Exceptions;
using Microsoft.Extensions.Logging;

namespace Edda.Agent.Providers.Embeddings;

/// <summary>
/// IEmbeddingService implementation using Voyage AI's voyage-3 model (1024 dimensions).
/// </summary>
public sealed class VoyageEmbeddingService : IEmbeddingService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _apiKey;
    private readonly ILogger<VoyageEmbeddingService> _logger;
    private readonly SemaphoreSlim _throttle = new(4, 4);

    /// <inheritdoc/>
    public int Dimensions => 1024;

    /// <inheritdoc/>
    public bool IsAvailable => true;

    /// <summary>
    /// Initializes a new VoyageEmbeddingService.
    /// </summary>
    /// <param name="httpClientFactory">Factory used to create HTTP clients.</param>
    /// <param name="apiKey">Voyage AI API key.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    public VoyageEmbeddingService(
        IHttpClientFactory httpClientFactory,
        string apiKey,
        ILogger<VoyageEmbeddingService> logger)
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
        using var client = _httpClientFactory.CreateClient("voyage-embedding");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        var response = await client.PostAsJsonAsync(
            "https://api.voyageai.com/v1/embeddings",
            new { model = "voyage-3", input = new[] { text } },
            ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            throw new ProviderException("voyage-embedding", "Embedding request failed.", (int)response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<VoyageEmbedResponse>(ct).ConfigureAwait(false);
        return result?.Data?.FirstOrDefault()?.Embedding ?? [];
    }

    private sealed class VoyageEmbedResponse
    {
        [JsonPropertyName("data")]
        public List<VoyageEmbedData>? Data { get; set; }
    }

    private sealed class VoyageEmbedData
    {
        [JsonPropertyName("embedding")]
        public float[]? Embedding { get; set; }
    }
}
