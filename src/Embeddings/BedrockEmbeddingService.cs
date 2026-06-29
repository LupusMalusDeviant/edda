using System.Text;
using System.Text.Json;
using Amazon;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Amazon.Runtime;
using Edda.Core.Abstractions;
using Edda.Core.Exceptions;
using Microsoft.Extensions.Logging;

namespace Edda.Agent.Providers.Embeddings;

/// <summary>
/// <see cref="IEmbeddingService"/> backed by Amazon Bedrock embedding models (Amazon Titan / Cohere) via
/// the AWS SDK (<c>InvokeModel</c>). Infrastructure adapter (real AWS SDK + SigV4) — the request/response
/// mapping is unit-tested via <see cref="BuildRequestBody"/>/<see cref="ParseEmbedding"/>, the network call
/// is not (consistent with the Bedrock chat client). Credentials: explicit access key + secret when both
/// are supplied, otherwise the default AWS credential chain (environment, profile, instance role).
/// </summary>
public sealed class BedrockEmbeddingService : IEmbeddingService
{
    private readonly string? _accessKeyId;
    private readonly string? _secretAccessKey;
    private readonly string _region;
    private readonly string _model;
    private readonly ILogger<BedrockEmbeddingService> _logger;

    /// <summary>Initializes a new instance of the <see cref="BedrockEmbeddingService"/> class.</summary>
    /// <param name="accessKeyId">AWS access key id; null with a null secret falls back to the default chain.</param>
    /// <param name="secretAccessKey">AWS secret access key; null with a null id falls back to the default chain.</param>
    /// <param name="region">AWS region (e.g. <c>us-east-1</c>).</param>
    /// <param name="model">Bedrock embedding model id (e.g. <c>amazon.titan-embed-text-v2:0</c>).</param>
    /// <param name="dimensions">Vector dimensions reported by this service.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    public BedrockEmbeddingService(
        string? accessKeyId,
        string? secretAccessKey,
        string region,
        string model,
        int dimensions,
        ILogger<BedrockEmbeddingService> logger)
    {
        _accessKeyId = accessKeyId;
        _secretAccessKey = secretAccessKey;
        _region = region;
        _model = model;
        Dimensions = dimensions;
        _logger = logger;
    }

    /// <inheritdoc />
    public int Dimensions { get; }

    /// <inheritdoc />
    public bool IsAvailable => true;

    /// <summary>Default vector dimensions for a known Bedrock embedding model.</summary>
    /// <param name="model">The Bedrock model id.</param>
    /// <returns>The model's native embedding dimensionality (fallback 1024).</returns>
    public static int DefaultDimensions(string model)
    {
        var normalized = model.ToLowerInvariant();
        if (normalized.Contains("titan-embed-text-v1"))
            return 1536;
        return 1024; // Titan v2 (default), Cohere v3, fallback.
    }

    /// <inheritdoc />
    public async Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        var regionEndpoint = RegionEndpoint.GetBySystemName(_region);
        using var client = CreateClient(regionEndpoint);

        var request = new InvokeModelRequest
        {
            ModelId = _model,
            ContentType = "application/json",
            Accept = "application/json",
            Body = new MemoryStream(Encoding.UTF8.GetBytes(BuildRequestBody(_model, text, Dimensions))),
        };

        InvokeModelResponse response;
        try
        {
            response = await client.InvokeModelAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (AmazonServiceException ex)
        {
            _logger.LogWarning(ex, "Bedrock embedding request failed");
            throw new ProviderException(
                "bedrock-embedding", $"Bedrock request failed: {ex.Message}", (int)ex.StatusCode, innerException: ex);
        }
        catch (AmazonClientException ex)
        {
            _logger.LogWarning(ex, "Bedrock embedding request failed");
            throw new ProviderException("bedrock-embedding", $"Bedrock request failed: {ex.Message}", innerException: ex);
        }

        using var reader = new StreamReader(response.Body, Encoding.UTF8);
        var json = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        return ParseEmbedding(json, _model);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<float[]>> EmbedBatchAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default)
    {
        // Sequential to stay within Bedrock per-account throughput limits.
        var results = new List<float[]>(texts.Count);
        foreach (var text in texts)
            results.Add(await EmbedAsync(text, cancellationToken).ConfigureAwait(false));
        return results;
    }

    /// <summary>Builds the InvokeModel request body for the model family (Titan or Cohere). Internal for tests.</summary>
    internal static string BuildRequestBody(string model, string text, int dimensions)
    {
        if (model.Contains("cohere", StringComparison.OrdinalIgnoreCase))
            return JsonSerializer.Serialize(new { texts = new[] { text }, input_type = "search_document" });

        if (model.Contains("titan-embed-text-v2", StringComparison.OrdinalIgnoreCase))
            return JsonSerializer.Serialize(new { inputText = text, dimensions, normalize = true });

        return JsonSerializer.Serialize(new { inputText = text });
    }

    /// <summary>
    /// Parses the embedding vector from a Bedrock response — Titan (<c>embedding</c>) or Cohere
    /// (<c>embeddings[0]</c>). Internal for unit testing. Returns an empty array if no vector is present.
    /// </summary>
    internal static float[] ParseEmbedding(string json, string model)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (root.TryGetProperty("embeddings", out var embeddings) && embeddings.ValueKind == JsonValueKind.Array)
        {
            var first = embeddings.EnumerateArray().FirstOrDefault();
            if (first.ValueKind == JsonValueKind.Array)
                return first.EnumerateArray().Select(e => e.GetSingle()).ToArray();
        }

        if (root.TryGetProperty("embedding", out var embedding) && embedding.ValueKind == JsonValueKind.Array)
            return embedding.EnumerateArray().Select(e => e.GetSingle()).ToArray();

        return [];
    }

    private AmazonBedrockRuntimeClient CreateClient(RegionEndpoint regionEndpoint)
    {
        if (!string.IsNullOrWhiteSpace(_accessKeyId) && !string.IsNullOrWhiteSpace(_secretAccessKey))
        {
            return new AmazonBedrockRuntimeClient(
                new BasicAWSCredentials(_accessKeyId, _secretAccessKey), regionEndpoint);
        }

        return new AmazonBedrockRuntimeClient(regionEndpoint);
    }
}
