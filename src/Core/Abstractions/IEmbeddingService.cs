namespace Edda.Core.Abstractions;

/// <summary>
/// Generates vector embeddings for semantic search in Phase 2 of context compilation.
/// NullEmbeddingService provides graceful degradation when no provider is configured.
/// </summary>
public interface IEmbeddingService
{
    /// <summary>
    /// Generates a vector embedding for a single text.
    /// </summary>
    /// <param name="text">The text to embed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Float array representing the text in embedding space.</returns>
    Task<float[]> EmbedAsync(
        string text,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Batch-embeds multiple texts with a maximum parallelism of 4 (internally enforced).
    /// </summary>
    /// <param name="texts">Texts to embed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Embedding vectors in the same order as the input texts.</returns>
    Task<IReadOnlyList<float[]>> EmbedBatchAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Embedding vector dimensions. Provider-dependent. Required for configuring the Neo4j Vector Index.
    /// </summary>
    int Dimensions { get; }

    /// <summary>
    /// False for NullEmbeddingService — Phase 2 (semantic boosting) is skipped when false.
    /// </summary>
    bool IsAvailable { get; }
}
