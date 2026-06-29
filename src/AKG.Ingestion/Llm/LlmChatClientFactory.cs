using Edda.Core.Exceptions;

namespace Edda.AKG.Ingestion.Llm;

/// <summary>
/// Default <see cref="ILlmChatClientFactory"/>. Maps a provider key to its concrete chat client,
/// applying per-provider default base URLs and model identifiers when the configuration omits them.
/// </summary>
public sealed class LlmChatClientFactory : ILlmChatClientFactory
{
    private readonly IHttpClientFactory _httpClientFactory;

    /// <summary>Initializes a new instance of the <see cref="LlmChatClientFactory"/> class.</summary>
    /// <param name="httpClientFactory">Factory used to create HTTP clients for HTTP-based providers.</param>
    public LlmChatClientFactory(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    /// <inheritdoc />
    public ILlmChatClient Create(LlmProviderConfig config)
    {
        var provider = (config.Provider ?? string.Empty).Trim().ToLowerInvariant();
        return provider switch
        {
            "anthropic" => new AnthropicChatClient(
                _httpClientFactory,
                Coalesce(config.BaseUrl, "https://api.anthropic.com"),
                config.ApiKey,
                Coalesce(config.Model, "claude-opus-4-8")),
            "openai" => new OpenAiCompatibleChatClient(
                "openai",
                _httpClientFactory,
                Coalesce(config.BaseUrl, "https://api.openai.com/v1"),
                config.ApiKey,
                Coalesce(config.Model, "gpt-4o")),
            "openrouter" => new OpenRouterChatClient(
                _httpClientFactory,
                Coalesce(config.BaseUrl, "https://openrouter.ai/api/v1"),
                config.ApiKey,
                Coalesce(config.Model, "meta-llama/llama-3.1-70b-instruct")),
            "ollama" => new OllamaChatClient(
                _httpClientFactory,
                Coalesce(config.BaseUrl, "http://localhost:11434"),
                Coalesce(config.Model, "llama3.1"),
                config.ApiKey),
            "gemini" => new GeminiChatClient(
                _httpClientFactory,
                Coalesce(config.BaseUrl, "https://generativelanguage.googleapis.com/v1beta"),
                config.ApiKey,
                Coalesce(config.Model, "gemini-1.5-flash")),
            "bedrock" => new AwsBedrockChatClient(
                config.AccessKeyId,
                config.ApiKey,
                Coalesce(config.Region, "us-east-1"),
                Coalesce(config.Model, "anthropic.claude-opus-4-8")),
            "custom" => new OpenAiCompatibleChatClient(
                "custom",
                _httpClientFactory,
                Coalesce(config.BaseUrl, string.Empty),
                config.ApiKey,
                Coalesce(config.Model, string.Empty)),
            _ => throw new ProviderException(provider, $"Unknown or unsupported LLM provider '{config.Provider}'."),
        };
    }

    private static string Coalesce(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;
}
