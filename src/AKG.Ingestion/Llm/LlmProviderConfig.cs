namespace Edda.AKG.Ingestion.Llm;

/// <summary>
/// Resolved configuration for constructing an <see cref="ILlmChatClient"/>: the provider key plus the
/// non-secret settings and the API key (resolved from the credential store at call time). Consumed by
/// <see cref="ILlmChatClientFactory"/>.
/// </summary>
public sealed record LlmProviderConfig
{
    /// <summary>
    /// Provider key (e.g. "anthropic", "openai", "ollama", "bedrock", "openrouter", "gemini", "custom").
    /// </summary>
    public required string Provider { get; init; }

    /// <summary>Model identifier; a null or empty value falls back to the provider's default.</summary>
    public string? Model { get; init; }

    /// <summary>API base URL; a null or empty value falls back to the provider's default.</summary>
    public string? BaseUrl { get; init; }

    /// <summary>
    /// API key or token; null for providers that need none (e.g. a local Ollama instance). For the
    /// Bedrock provider this carries the AWS secret access key (paired with <see cref="AccessKeyId"/>).
    /// </summary>
    public string? ApiKey { get; init; }

    /// <summary>AWS access key id for the Bedrock provider; ignored by other providers.</summary>
    public string? AccessKeyId { get; init; }

    /// <summary>AWS region for the Bedrock provider; ignored by other providers.</summary>
    public string? Region { get; init; }
}
