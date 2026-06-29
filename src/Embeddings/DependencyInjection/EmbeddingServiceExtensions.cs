using Edda.Core.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Edda.Embeddings.DependencyInjection;

/// <summary>
/// Extension methods for registering the configured <see cref="IEmbeddingService"/> with the DI
/// container. Unlike the original Edda wiring (which only knew openai/ollama/null), this
/// standalone variant supports all six embedding providers so they are reachable from a single
/// <c>EMBEDDING_PROVIDER</c> switch.
/// </summary>
public static class EmbeddingServiceExtensions
{
    /// <summary>
    /// All embedding provider keys understood by <see cref="AddEmbeddingService"/>.
    /// Useful for populating provider selectors in the UI.
    /// </summary>
    public static readonly IReadOnlyList<string> AvailableProviders =
        ["openai", "google", "voyage", "ollama", "custom", "bedrock", "null"];

    /// <summary>
    /// Registers the live embedding stack: <see cref="IEmbeddingProviderFactory"/> and a
    /// <see cref="ResolvingEmbeddingService"/> registered as <see cref="IEmbeddingService"/>. The resolving
    /// service resolves the provider, model and key at call time from <see cref="ISettingsService"/> and the
    /// credential store, falling back to <c>EMBEDDING_*</c> / <c>Embeddings:*</c> configuration. A single
    /// instance serves both DB indexing and query embedding, so the vectors always share one space; provider
    /// and model changes take effect without a restart, though stored DB embeddings remain until a re-embed
    /// (see ADR-0004).
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="configuration">
    /// Retained for call-site compatibility; runtime settings now come from <see cref="ISettingsService"/>
    /// and the resolving service reads environment fallback from the ambient <see cref="IConfiguration"/>.
    /// </param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddEmbeddingService(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
        services.AddHttpClient();
        services.AddSingleton<IEmbeddingProviderFactory, EmbeddingProviderFactory>();
        services.AddSingleton<IEmbeddingService, ResolvingEmbeddingService>();
        return services;
    }
}
