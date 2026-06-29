namespace Edda.Core.Models;

/// <summary>
/// A short-term memory entry annotated with its individual and combined relevance scores
/// from the hybrid search pipeline (BM25 + vector + temporal decay).
/// </summary>
/// <param name="Entry">The underlying memory entry.</param>
/// <param name="BM25Score">
/// Normalized BM25 keyword-relevance score (0–1, higher = better).
/// 0 when the entry contains no query terms.
/// </param>
/// <param name="VectorScore">
/// Cosine-similarity score between the query embedding and the entry embedding (0–1).
/// 0 when embeddings are unavailable or the entry has no stored embedding.
/// </param>
/// <param name="TemporalScore">
/// Recency score based on exponential decay (0–1).
/// 1 = just created; halves every <c>halfLifeDays</c> days.
/// </param>
/// <param name="CombinedScore">
/// Weighted combination of the three scores used for final ranking and MMR input.
/// Default weights: BM25=0.3, Vector=0.5, Temporal=0.2.
/// </param>
public sealed record ScoredMemoryEntry(
    ShortTermMemoryEntry Entry,
    double BM25Score,
    double VectorScore,
    double TemporalScore,
    double CombinedScore);
