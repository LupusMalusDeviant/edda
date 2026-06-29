namespace Edda.Embeddings;

/// <summary>
/// Resolved configuration for constructing an <see cref="Edda.Core.Abstractions.IEmbeddingService"/>:
/// the provider key plus the non-secret settings and the API key (resolved from the credential store).
/// Consumed by <see cref="IEmbeddingProviderFactory"/>.
/// </summary>
public sealed record EmbeddingProviderConfig
{
    /// <summary>Provider key (openai | google | voyage | ollama | custom | null).</summary>
    public required string Provider { get; init; }

    /// <summary>API key for hosted providers; null or empty for providers that need none (e.g. local Ollama).</summary>
    public string? ApiKey { get; init; }

    /// <summary>API base URL (Ollama/Custom); null falls back to the provider default.</summary>
    public string? BaseUrl { get; init; }

    /// <summary>Model identifier; null falls back to the provider default.</summary>
    public string? Model { get; init; }

    /// <summary>Vector dimensions (Ollama/Custom); null falls back to the provider default.</summary>
    public int? Dimensions { get; init; }

    /// <summary>AWS region (Bedrock); null falls back to the provider default.</summary>
    public string? Region { get; init; }

    /// <summary>AWS access key id (Bedrock); null with a null key lets the AWS SDK use its default credential chain.</summary>
    public string? AccessKeyId { get; init; }
}
