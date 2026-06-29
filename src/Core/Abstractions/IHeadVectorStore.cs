using Edda.Core.Models;

namespace Edda.Core.Abstractions;

/// <summary>
/// Stores and searches per-head vector representations (centroids of a head's descendant chunk embeddings)
/// for stage 1 of hierarchical coarse-to-fine retrieval: the query is matched against head centroids to
/// pre-prune to the most relevant repository / upload subtrees before the fine-grained chunk search runs.
/// See ADR-0009.
/// </summary>
public interface IHeadVectorStore
{
    /// <summary>
    /// Ensures the native vector index over head centroids exists. Best-effort — a no-op (and app-side
    /// cosine fallback at query time) on providers without vector-index support.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    Task EnsureIndexAsync(CancellationToken ct);

    /// <summary>
    /// Recomputes the centroid set for every head from its descendant chunk embeddings and persists them.
    /// Heads without embedded chunks are skipped. Safe to re-run; idempotent per head.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    Task RebuildAsync(CancellationToken ct);

    /// <summary>
    /// Returns the top-<paramref name="topK"/> heads whose centroids are most similar to the query
    /// embedding and above the cosine <paramref name="threshold"/>, respecting user scope.
    /// </summary>
    /// <param name="queryEmbedding">The query vector.</param>
    /// <param name="topK">Maximum number of heads to return.</param>
    /// <param name="threshold">Minimum cosine similarity for a head to qualify.</param>
    /// <param name="userId">User scope; <see langword="null"/> returns only global heads.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Matched heads, highest score first; empty when nothing qualifies.</returns>
    Task<IReadOnlyList<HeadMatch>> FindTopHeadsAsync(
        float[] queryEmbedding, int topK, double threshold, string? userId, CancellationToken ct);

    /// <summary>
    /// Returns head-vector coverage: the number of repository/upload heads that already have centroids and
    /// the total number of such heads (for status / progress reporting).
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A tuple of (HeadsWithVectors, TotalHeads).</returns>
    Task<(int HeadsWithVectors, int TotalHeads)> GetCoverageAsync(CancellationToken ct);
}
