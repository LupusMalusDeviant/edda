namespace Edda.Core.Abstractions;

/// <summary>
/// Persistence contract for the vector/embedding layer (ADR-0013, second seam): approximate-nearest-neighbour
/// search over chunk embeddings plus retrieval of stored chunk embeddings, both mapped back to the parent rule.
/// Lets a non-Cypher vector backend (a dedicated vector DB, SQLite + ANN, …) replace the native Neo4j vector
/// index without touching the retrieval logic (RRF/MMR/app-side fallback) that composes on top.
/// </summary>
public interface IVectorStore
{
    /// <summary>
    /// Approximate-nearest-neighbour search: returns the parent rules whose chunks are most similar to
    /// <paramref name="queryVector"/> (score above <paramref name="threshold"/>), scoped to the caller and
    /// keyed by rule id with the best chunk score. Throws when the backend has no usable vector index
    /// (missing, or unsupported by the provider — e.g. Memgraph); the caller then falls back to app-side scoring.
    /// </summary>
    /// <param name="queryVector">The query embedding.</param>
    /// <param name="topK">Number of nearest chunks to retrieve from the index.</param>
    /// <param name="threshold">Minimum similarity score (exclusive).</param>
    /// <param name="userId">User scope; null returns only global rules.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Rule id → best similarity score, for matches above the threshold.</returns>
    Task<IReadOnlyDictionary<string, double>> SearchByVectorAsync(
        float[] queryVector,
        int topK,
        double threshold,
        string? userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns one representative embedding (the lowest-ordinal chunk) per rule — used to diversify the top
    /// candidates at the document level (MMR).
    /// </summary>
    /// <param name="ruleIds">The rules to fetch representative embeddings for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Rule id → representative chunk embedding (rules without an embedding are omitted).</returns>
    Task<IReadOnlyDictionary<string, float[]>> GetRepresentativeEmbeddingsAsync(
        IReadOnlyList<string> ruleIds,
        CancellationToken cancellationToken = default);

    /// <summary>Returns all chunk embeddings grouped by parent rule id (for app-side cosine scoring).</summary>
    /// <param name="ruleIds">The rules to fetch chunk embeddings for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Rule id → all chunk embeddings (rules without embeddings are omitted).</returns>
    Task<IReadOnlyDictionary<string, IReadOnlyList<float[]>>> GetChunkEmbeddingsAsync(
        IReadOnlyList<string> ruleIds,
        CancellationToken cancellationToken = default);
}
