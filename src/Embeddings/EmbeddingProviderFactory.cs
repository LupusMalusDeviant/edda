using Edda.Agent.Providers.Embeddings;
using Edda.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace Edda.Embeddings;

/// <summary>
/// Default <see cref="IEmbeddingProviderFactory"/>. Maps a provider key to its concrete embedding service,
/// applying per-provider default base URLs, models and dimensions when the configuration omits them.
/// </summary>
public sealed class EmbeddingProviderFactory : IEmbeddingProviderFactory
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILoggerFactory _loggerFactory;
    private readonly TimeProvider _timeProvider;

    /// <summary>Initializes a new instance of the <see cref="EmbeddingProviderFactory"/> class.</summary>
    /// <param name="httpClientFactory">Factory used to create HTTP clients for hosted providers.</param>
    /// <param name="loggerFactory">Factory used to create per-provider loggers.</param>
    /// <param name="timeProvider">Time provider required by the Ollama provider.</param>
    public EmbeddingProviderFactory(
        IHttpClientFactory httpClientFactory,
        ILoggerFactory loggerFactory,
        TimeProvider timeProvider)
    {
        _httpClientFactory = httpClientFactory;
        _loggerFactory = loggerFactory;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public IEmbeddingService Create(EmbeddingProviderConfig config)
    {
        var provider = (config.Provider ?? "null").Trim().ToLowerInvariant();
        return provider switch
        {
            "openai" => new OpenAiEmbeddingService(
                _httpClientFactory, config.ApiKey ?? string.Empty,
                _loggerFactory.CreateLogger<OpenAiEmbeddingService>()),
            "google" => new GoogleEmbeddingService(
                _httpClientFactory, config.ApiKey ?? string.Empty,
                _loggerFactory.CreateLogger<GoogleEmbeddingService>()),
            "voyage" => new VoyageEmbeddingService(
                _httpClientFactory, config.ApiKey ?? string.Empty,
                _loggerFactory.CreateLogger<VoyageEmbeddingService>()),
            "ollama" => new OllamaEmbeddingService(
                _httpClientFactory,
                _timeProvider,
                _loggerFactory.CreateLogger<OllamaEmbeddingService>(),
                baseUrl: Coalesce(config.BaseUrl, "http://localhost:11434"),
                model: Coalesce(config.Model, OllamaEmbeddingService.DefaultModel),
                dimensions: config.Dimensions ?? OllamaEmbeddingService.DefaultDimensions,
                apiKey: config.ApiKey),
            "custom" => new CustomEmbeddingService(
                _httpClientFactory,
                baseUrl: Coalesce(config.BaseUrl, "http://localhost:11434/v1"),
                apiKey: config.ApiKey ?? string.Empty,
                model: Coalesce(config.Model, "text-embedding-3-small"),
                dimensions: config.Dimensions ?? 1536,
                _loggerFactory.CreateLogger<CustomEmbeddingService>()),
            "bedrock" or "aws" => new BedrockEmbeddingService(
                accessKeyId: config.AccessKeyId,
                secretAccessKey: config.ApiKey,
                region: Coalesce(config.Region, "us-east-1"),
                model: Coalesce(config.Model, "amazon.titan-embed-text-v2:0"),
                dimensions: config.Dimensions
                    ?? BedrockEmbeddingService.DefaultDimensions(Coalesce(config.Model, "amazon.titan-embed-text-v2:0")),
                _loggerFactory.CreateLogger<BedrockEmbeddingService>()),
            _ => new NullEmbeddingService(),
        };
    }

    private static string Coalesce(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;
}
