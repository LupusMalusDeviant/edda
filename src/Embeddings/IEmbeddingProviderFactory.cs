using Edda.Core.Abstractions;

namespace Edda.Embeddings;

/// <summary>
/// Builds <see cref="IEmbeddingService"/> instances from an <see cref="EmbeddingProviderConfig"/>.
/// Centralizes provider selection so the embedding model can be resolved at runtime (see ADR-0004).
/// </summary>
public interface IEmbeddingProviderFactory
{
    /// <summary>
    /// Creates an embedding service for the given configuration; an unknown provider yields a no-op
    /// (null) service so retrieval degrades gracefully.
    /// </summary>
    /// <param name="config">The resolved provider configuration.</param>
    /// <returns>An <see cref="IEmbeddingService"/> for the configured provider.</returns>
    IEmbeddingService Create(EmbeddingProviderConfig config);
}
