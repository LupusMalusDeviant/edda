namespace Edda.Core.Models;

/// <summary>
/// Configuration for an LLM provider connection.
/// Consumed by ProviderFactory to construct the appropriate IModelClient implementation.
/// </summary>
public sealed record AgentConfig
{
    /// <summary>
    /// Provider identifier. Supported values: "anthropic", "openai", "mistral", "deepseek",
    /// "ollama", "google", "openrouter", "azure-openai", or any custom value for
    /// OpenAI-compatible endpoints. Falls back to the LLM_PROVIDER environment variable.
    /// </summary>
    public string? Provider { get; init; }

    /// <summary>
    /// API key for authentication with the provider. Not required for local providers like Ollama.
    /// Must be retrieved from ICredentialStore in production — never hardcoded.
    /// </summary>
    public string? ApiKey { get; init; }

    /// <summary>
    /// Model identifier to use for completions (e.g. "claude-sonnet-4-6", "gpt-4o").
    /// </summary>
    public string Model { get; init; } = "claude-sonnet-4-6";

    /// <summary>
    /// Base URL override for custom or self-hosted endpoints.
    /// Used by CustomOpenAiCompatibleClient and OllamaClient.
    /// </summary>
    public string? BaseUrl { get; init; }

    /// <summary>
    /// Azure OpenAI deployment name. Only used when Provider is "azure-openai".
    /// </summary>
    public string? AzureDeploymentName { get; init; }

    /// <summary>
    /// Azure OpenAI API version string (e.g. "2024-02-01").
    /// Only used when Provider is "azure-openai".
    /// </summary>
    public string? AzureApiVersion { get; init; }
}
