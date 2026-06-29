using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Edda.Core.Abstractions;
using Edda.Core.Exceptions;
using Microsoft.Extensions.Logging;

namespace Edda.Agent.Providers.Embeddings;

/// <summary>
/// IEmbeddingService implementation for any OpenAI-compatible embedding endpoint.
/// Dimensions, model name, and base URL are fully configurable.
/// </summary>
public sealed class CustomEmbeddingService : IEmbeddingService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _baseUrl;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly ILogger<CustomEmbeddingService> _logger;
    private readonly SemaphoreSlim _throttle = new(4, 4);

    /// <inheritdoc/>
    public int Dimensions { get; }

    /// <inheritdoc/>
    public bool IsAvailable => true;

    /// <summary>
    /// Initializes a new CustomEmbeddingService.
    /// </summary>
    /// <param name="httpClientFactory">Factory used to create HTTP clients.</param>
    /// <param name="baseUrl">Base URL of the embedding endpoint.</param>
    /// <param name="apiKey">API key for authentication. Empty for local services.</param>
    /// <param name="model">Model identifier.</param>
    /// <param name="dimensions">Number of dimensions in the output embedding.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    public CustomEmbeddingService(
        IHttpClientFactory httpClientFactory,
        string baseUrl,
        string apiKey,
        string model,
        int dimensions,
        ILogger<CustomEmbeddingService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _baseUrl = baseUrl.TrimEnd('/');
        _apiKey = apiKey;
        _model = model;
        Dimensions = dimensions;
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
        using var client = _httpClientFactory.CreateClient("custom-embedding");
        if (!string.IsNullOrEmpty(_apiKey))
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        var response = await client.PostAsJsonAsync(
            $"{_baseUrl}/embeddings",
            new { input = text, model = _model },
            ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            throw new ProviderException("custom-embedding", "Embedding request failed.", (int)response.StatusCode);

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
