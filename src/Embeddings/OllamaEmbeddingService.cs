using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Edda.Core.Abstractions;
using Edda.Core.Exceptions;
using Microsoft.Extensions.Logging;

namespace Edda.Agent.Providers.Embeddings;

/// <summary>
/// IEmbeddingService implementation using Ollama's local embedding API
/// (nomic-embed-text-v2-moe, 768 dimensions).
/// Includes a circuit-breaker: after <see cref="FailureThreshold"/> consecutive failures the service
/// marks itself unavailable for <see cref="CooldownSeconds"/> seconds before probing again.
/// </summary>
public sealed class OllamaEmbeddingService : IEmbeddingService
{
    /// <summary>Number of consecutive failures before the circuit opens.</summary>
    public const int FailureThreshold = 3;

    /// <summary>Seconds the circuit stays open before a probe attempt is allowed.</summary>
    public const int CooldownSeconds = 60;

    /// <summary>Per-request HTTP timeout in seconds — fail fast instead of the 100 s HttpClient default.</summary>
    private const int RequestTimeoutSeconds = 30;

    /// <summary>Default model when none is configured via environment variable.</summary>
    public const string DefaultModel = "nomic-embed-text-v2-moe";

    /// <summary>Default embedding dimensions for the default model (nomic-embed-text-v2-moe = 768).</summary>
    public const int DefaultDimensions = 768;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _baseUrl;
    private readonly string _model;
    private readonly string? _apiKey;
    private readonly int _dimensions;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<OllamaEmbeddingService> _logger;
    private readonly SemaphoreSlim _throttle = new(4, 4);

    // Circuit-breaker state — written via Interlocked for thread safety.
    private int _consecutiveFailures;
    private long _disabledUntilTicks; // 0 = circuit closed

    /// <inheritdoc/>
    public int Dimensions => _dimensions;

    /// <inheritdoc/>
    /// <remarks>
    /// Returns <see langword="false"/> while the circuit is open (i.e. after
    /// <see cref="FailureThreshold"/> consecutive failures and within the cooldown window).
    /// Automatically resets to <see langword="true"/> once the cooldown expires so that the
    /// next call can probe whether Ollama has recovered.
    /// </remarks>
    public bool IsAvailable
    {
        get
        {
            var until = Interlocked.Read(ref _disabledUntilTicks);
            if (until == 0) return true;
            return _timeProvider.GetUtcNow().UtcTicks > until;
        }
    }

    /// <summary>
    /// Initializes a new <see cref="OllamaEmbeddingService"/>.
    /// </summary>
    /// <param name="httpClientFactory">Factory used to create HTTP clients.</param>
    /// <param name="timeProvider">Time provider for circuit-breaker cooldown tracking.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    /// <param name="baseUrl">Ollama base URL. Defaults to <c>http://localhost:11434</c>.</param>
    /// <param name="model">Ollama embedding model name. Defaults to <c>nomic-embed-text-v2-moe</c>.</param>
    /// <param name="dimensions">Embedding vector dimensions. Defaults to 768 (nomic-embed-text-v2-moe).</param>
    /// <param name="apiKey">Optional bearer token for secured/hosted Ollama endpoints (null for a local instance).</param>
    public OllamaEmbeddingService(
        IHttpClientFactory httpClientFactory,
        TimeProvider timeProvider,
        ILogger<OllamaEmbeddingService> logger,
        string baseUrl = "http://localhost:11434",
        string model = DefaultModel,
        int dimensions = DefaultDimensions,
        string? apiKey = null)
    {
        _httpClientFactory = httpClientFactory;
        _timeProvider      = timeProvider;
        _baseUrl           = baseUrl.TrimEnd('/');
        _model             = model;
        _dimensions        = dimensions;
        _apiKey            = apiKey;
        _logger            = logger;
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
        using var client = _httpClientFactory.CreateClient("ollama-embedding");
        client.Timeout = TimeSpan.FromSeconds(RequestTimeoutSeconds);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/api/embed")
        {
            Content = JsonContent.Create(new { model = _model, input = text }),
        };
        if (!string.IsNullOrWhiteSpace(_apiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        HttpResponseMessage response;

        try
        {
            response = await client.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Genuine cancellation (e.g. the user aborted the rebuild) — propagate, do not count as a failure.
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            RecordFailure($"HTTP error: {ex.Message}");
            throw new ProviderException("ollama-embedding", $"Embedding request failed: {ex.Message}", 0);
        }

        if (!response.IsSuccessStatusCode)
        {
            RecordFailure($"HTTP {(int)response.StatusCode}");
            throw new ProviderException("ollama-embedding", "Embedding request failed.", (int)response.StatusCode);
        }

        var result = await response.Content.ReadFromJsonAsync<OllamaEmbedResponse>(ct).ConfigureAwait(false);
        RecordSuccess();
        return result?.Embeddings?.FirstOrDefault() ?? [];
    }

    /// <summary>Records a successful embedding call and closes the circuit.</summary>
    private void RecordSuccess()
    {
        Interlocked.Exchange(ref _consecutiveFailures, 0);
        Interlocked.Exchange(ref _disabledUntilTicks, 0);
    }

    /// <summary>
    /// Records a failed embedding call. Opens the circuit after <see cref="FailureThreshold"/>
    /// consecutive failures.
    /// </summary>
    private void RecordFailure(string reason)
    {
        var failures = Interlocked.Increment(ref _consecutiveFailures);

        if (failures >= FailureThreshold)
        {
            var until = _timeProvider.GetUtcNow().AddSeconds(CooldownSeconds).UtcTicks;
            Interlocked.Exchange(ref _disabledUntilTicks, until);

            _logger.LogWarning(
                "Ollama embedding circuit opened after {Failures} consecutive failures ({Reason}). "
                + "IsAvailable=false for {Cooldown}s | Embeddings",
                failures, reason, CooldownSeconds);
        }
    }

    private sealed class OllamaEmbedResponse
    {
        [JsonPropertyName("embeddings")]
        public List<float[]>? Embeddings { get; set; }
    }
}
